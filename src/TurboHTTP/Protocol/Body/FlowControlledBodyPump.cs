using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Protocol.Body;

internal sealed class FlowControlledBodyPump : BodyPumpBase<int>
{
    private readonly FlowController _flowController;
    private readonly HashSet<int> _windowBlockedStreams = new();

    public FlowControlledBodyPump(
        IBodyDrainTarget<int> target,
        FlowController flowController,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts)
        : base(target, poolContext, connectionCts)
    {
        _flowController = flowController;
    }

    /// <summary>
    /// Registers a body that was partially serialized synchronously and could not be fully sent
    /// due to an exhausted send window. The remainder data is wrapped in a MemoryStream and
    /// registered via the base pump so it drains when the window opens.
    /// </summary>
    public void RegisterWithLimbo(int streamId, ReadOnlyMemory<byte> remainder, CancellationToken requestCt)
    {
        // Copy the remainder into a byte array so the MemoryStream owns the data
        // independently of the caller's buffer lifetime.
        var copy = remainder.ToArray();
        var stream = new MemoryStream(copy, writable: false);
        base.Register(streamId, stream, copy.Length, requestCt);

        // Bootstrap credits — stream starts blocked if window is zero; credits allow a read
        // attempt once the window opens via OnWindowUpdate.
        for (var i = 0; i < 16; i++)
        {
            AddCredit();
        }
    }

    public new void Register(int streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        base.Register(streamId, bodyStream, contentLength, requestCt);
        // Bootstrap with enough credits to drain synchronous streams without additional calls.
        for (var i = 0; i < 16; i++)
        {
            AddCredit();
        }
    }

    public void OnWindowUpdate(int streamId)
    {
        if (streamId == 0)
        {
            // Connection-level update: re-evaluate all blocked streams and unblock eligible ones.
            var unblocked = new List<int>();
            foreach (var blocked in _windowBlockedStreams)
            {
                var window = Math.Min(
                    _flowController.GetStreamSendWindow(blocked),
                    _flowController.ConnectionSendWindow);
                if (window >= ComputeMinReadSize())
                {
                    unblocked.Add(blocked);
                }
            }

            foreach (var id in unblocked)
            {
                _windowBlockedStreams.Remove(id);
                EnqueueStream(id);
            }
        }
        else if (_windowBlockedStreams.Remove(streamId))
        {
            EnqueueStream(streamId);
        }

        // Deadlock prevention: if credits are available and streams were just unblocked, boost credits to
        // ensure all unblocked streams can fully drain. Each stream may need several read rounds.
        if (GetCredits() > 0)
        {
            for (var i = 0; i < 16; i++)
            {
                AddCredit();
            }
        }
    }

    public void Cleanup() => CancelAll();

    protected override BodyDrainSlot<int> RentSlot()
        => PoolContext.Rent(static () => new FlowControlledDrainSlot());

    protected override void ReturnSlot(BodyDrainSlot<int> slot)
        => PoolContext.Return((FlowControlledDrainSlot)slot);

    protected override bool IsStreamEligible(int streamId, BodyDrainSlot<int> slot)
    {
        var available = Math.Min(
            _flowController.GetStreamSendWindow(streamId),
            _flowController.ConnectionSendWindow);

        if (available < ComputeMinReadSize())
        {
            _windowBlockedStreams.Add(streamId);
            return false;
        }

        return true;
    }

    protected override int ComputeReadSize(int streamId, BodyDrainSlot<int> slot)
    {
        var chunkSize = Target.PreferredChunkSize;
        var available = Math.Min(
            _flowController.GetStreamSendWindow(streamId),
            _flowController.ConnectionSendWindow);
        return (int)Math.Min(chunkSize, available);
    }

    protected override void BeforeRead(int streamId, BodyDrainSlot<int> slot)
    {
        var reserve = ComputeReadSize(streamId, slot);
        _flowController.Reserve(streamId, reserve);
        ((FlowControlledDrainSlot)slot).ReservedWindow = reserve;
    }

    protected override void AfterRead(int streamId, BodyDrainSlot<int> slot, int bytesRead)
    {
        var fcSlot = (FlowControlledDrainSlot)slot;
        var refund = fcSlot.ReservedWindow - bytesRead;
        if (refund > 0)
        {
            _flowController.Refund(streamId, refund);
        }

        fcSlot.ReservedWindow = 0;
    }

    protected override void OnCancelAll()
    {
        _windowBlockedStreams.Clear();
    }

    protected override void OnStreamCancelled(int streamId)
    {
        _windowBlockedStreams.Remove(streamId);
    }

    private int ComputeMinReadSize() => Target.PreferredChunkSize / 2;
}
