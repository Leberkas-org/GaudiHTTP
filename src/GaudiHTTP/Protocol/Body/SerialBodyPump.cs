using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Body;

internal sealed class SerialBodyPump(
    IBodyDrainTarget target,
    CancellationTokenSource connectionCts,
    int chunkSize,
    int maxBytes)
{
    private Stream? _activeStream;
    private IMemoryOwner<byte>? _activeOwner;
    private CancellationTokenSource? _linkedCts;
    private bool _isReadInFlight;

    // Credit is denominated in body bytes, not chunk count: the pump may read while the budget is
    // positive and debits the actual bytes read at emit time (below). A real wire flush of N bytes
    // (TransportDataFlushed) credits N bytes back via OnCapacityAvailable, clamped to maxBytes, so
    // at most maxBytes of body is ever in flight ahead of the socket. A negative budget (a final
    // read overshooting the remaining credit) is expected and self-heals on the next credit.
    private long _availableBytes;

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
        _availableBytes = maxBytes;
        TryStartRead();
    }

    public void OnCapacityAvailable(int bytes)
    {
        _availableBytes = Math.Min(maxBytes, _availableBytes + bytes);
        Tracing.For("Protocol").Trace(this, "serial body credit={0} budget={1}", bytes, _availableBytes);
        TryStartRead();
    }

    public void ResetCredit()
    {
        _availableBytes = maxBytes;
        Tracing.For("Protocol").Debug(this, "serial body credit reset to {0}", maxBytes);
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
        _availableBytes = 0;
        _isReadInFlight = false;
    }

    public void Cleanup()
    {
        _activeOwner?.Dispose();
        _activeOwner = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableBytes = 0;
        _isReadInFlight = false;
    }

    private void TryStartRead()
    {
        if (_availableBytes <= 0 || _isReadInFlight || _activeStream is null)
        {
            return;
        }

        var token = _linkedCts?.Token ?? connectionCts.Token;
        _isReadInFlight = true;
        // WireBuffer.Rent leaves Length unset (0); ReadAsync below slices Memory[..chunkSize], so
        // Length must span the full rented capacity, matching the deleted PooledArrayMemoryOwner's
        // semantics.
        var buffer = WireBuffer.Rent(chunkSize);
        buffer.Length = buffer.Capacity;
        _activeOwner = buffer;
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

        _availableBytes -= bytesRead;
        target.EmitOwnedDataFrames(0, owner!, bytesRead, endStream: false);
        Tracing.For("Protocol").Trace(this, "serial body debit={0} budget={1}", bytesRead, _availableBytes);
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
        _availableBytes = 0;
        if (wasActive)
        {
            target.OnDrainComplete(0);
        }
    }
}
