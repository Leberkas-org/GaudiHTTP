using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;

namespace GaudiHTTP.Protocol.Body;

internal sealed class SerialBodyPump(
    IBodyDrainTarget target,
    CancellationTokenSource connectionCts,
    int chunkSize,
    int maxCapacity)
{
    private Stream? _activeStream;
    private IMemoryOwner<byte>? _activeOwner;
    private CancellationTokenSource? _linkedCts;
    private bool _isReadInFlight;
    private int _availableCapacity;

    // Serial stream id is always 0, so the read-completion transforms capture nothing and are
    // shared statically — no per-Register closure allocation.
    private static readonly Func<int, object> CachedSuccess = n => new BodyReadComplete<int>(0, n);
    private static readonly Func<Exception, object> CachedFailure = ex => new BodyReadFailed<int>(0, ex);

    public void Register(Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        _activeStream = bodyStream;
        _linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(connectionCts.Token, requestCt)
            : null;
        _availableCapacity = maxCapacity;
        TryStartRead();
    }

    public void OnCapacityAvailable()
    {
        if (_availableCapacity < maxCapacity)
        {
            _availableCapacity++;
        }

        TryStartRead();
    }

    public void HandleReadComplete(int bytesRead)
    {
        _isReadInFlight = false;
        ProcessReadResult(bytesRead);
    }

    public void HandleReadFailed(Exception reason)
    {
        _isReadInFlight = false;
        _activeOwner?.Dispose();
        _activeOwner = null;
        _activeStream = null;
        target.OnDrainFailed(0, reason);
        CompleteDrain();
    }

    public void Cancel()
    {
        _linkedCts?.Cancel();
        _activeOwner?.Dispose();
        _activeOwner = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableCapacity = 0;
        _isReadInFlight = false;
    }

    public void Cleanup()
    {
        _activeOwner?.Dispose();
        _activeOwner = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableCapacity = 0;
        _isReadInFlight = false;
    }

    private void TryStartRead()
    {
        if (_availableCapacity <= 0 || _isReadInFlight || _activeStream is null)
        {
            return;
        }

        _availableCapacity--;
        var token = _linkedCts?.Token ?? connectionCts.Token;
        _isReadInFlight = true;
        _activeOwner = PooledArrayMemoryOwner.Create(chunkSize);
        var vt = _activeStream.ReadAsync(_activeOwner.Memory[..chunkSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            // Force-async: the bytes sit in _activeOwner until the Tell'd completion is
            // processed. Keep _isReadInFlight = true across the mailbox hop (exactly like the
            // PipeTo path below) so an interleaved OnCapacityAvailable -> TryStartRead cannot
            // start the next read before HandleReadComplete hands off the current owner.
            target.StageActor.Tell(CachedSuccess(vt.Result), ActorRefs.NoSender);
            return;
        }

        vt.PipeTo(
            target.StageActor,
            success: CachedSuccess,
            failure: CachedFailure);
    }

    private void ProcessReadResult(int bytesRead)
    {
        var owner = _activeOwner;
        _activeOwner = null;

        if (bytesRead == 0)
        {
            owner?.Dispose();
            target.EmitDataFrames(0, default, endStream: true);
            CompleteDrain();
            return;
        }

        target.EmitOwnedDataFrames(0, owner!, bytesRead, endStream: false);
        TryStartRead();
    }

    private void CompleteDrain()
    {
        var wasActive = _activeStream is not null;
        _activeOwner?.Dispose();
        _activeOwner = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableCapacity = 0;
        if (wasActive)
        {
            target.OnDrainComplete(0);
        }
    }
}
