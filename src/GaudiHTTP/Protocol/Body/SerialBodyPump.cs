using System.Buffers;
using Akka.Actor;
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

    // Set by Cancel/Cleanup when a body read is still in flight: the pending ReadAsync still targets
    // _activeOwner's pooled array, so disposing it now would recycle the array under the read (buffer
    // UAF) and the late completion would dereference a nulled owner (NRE). Instead we defer the
    // dispose+teardown to HandleReadComplete/HandleReadFailed, mirroring the MarkOrphaned/defer
    // discipline the multiplexed pumps use in Cancel.
    private bool _teardownPending;

    // Credit is denominated in body bytes, not chunk count: the pump may read while the budget is
    // positive and debits the actual bytes read at emit time (below). A pipe flush credits bytes
    // back via OnCapacityAvailable, clamped to maxBytes, so at most maxBytes of body is ever in
    // flight ahead of the socket. A negative budget (a final read overshooting the remaining
    // credit) is expected and self-heals on the next credit.
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

    public void ParkForFlush()
    {
        _availableBytes = 0;
    }

    public void HandleReadComplete(int bytesRead)
    {
        _isReadInFlight = false;

        if (_teardownPending)
        {
            FinishDeferredTeardown();
            return;
        }

        ProcessReadResult(bytesRead);
    }

    public void HandleReadFailed(Exception reason)
    {
        _isReadInFlight = false;

        if (_teardownPending)
        {
            FinishDeferredTeardown();
            return;
        }

        _activeOwner?.Dispose();
        _activeOwner = null;
        _activeStream = null;
        target.OnDrainFailed(0, reason);
        CompleteDrain();
    }

    public void Cancel()
    {
        _linkedCts?.Cancel();

        if (_isReadInFlight)
        {
            // Read still outstanding: defer dispose+teardown to the completion so the in-flight
            // ReadAsync does not write into a recycled pool array and the late completion does not
            // dereference a nulled owner. LinkedCts stays alive (cancelled above) so the read observes
            // cancellation; it is disposed by FinishDeferredTeardown when the read lands.
            _teardownPending = true;
            _availableBytes = 0;
            return;
        }

        _activeOwner?.Dispose();
        _activeOwner = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableBytes = 0;
    }

    public void Cleanup()
    {
        if (_isReadInFlight)
        {
            // Connection teardown with a read in flight: defer exactly as Cancel does. The caller
            // cancels the connection CTS immediately after Cleanup, which completes the outstanding
            // read and drives FinishDeferredTeardown.
            _teardownPending = true;
            _availableBytes = 0;
            return;
        }

        _activeOwner?.Dispose();
        _activeOwner = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableBytes = 0;
    }

    private void FinishDeferredTeardown()
    {
        _teardownPending = false;
        _activeOwner?.Dispose();
        _activeOwner = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableBytes = 0;
    }

    private void TryStartRead()
    {
        if (_availableBytes <= 0 || _isReadInFlight || _activeStream is null)
        {
            return;
        }

        var token = _linkedCts?.Token ?? connectionCts.Token;
        _isReadInFlight = true;
        _activeOwner = MemoryPool<byte>.Shared.Rent(chunkSize);
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
