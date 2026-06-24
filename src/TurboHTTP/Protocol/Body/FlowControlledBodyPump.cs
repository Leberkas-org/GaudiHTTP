using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Protocol.Body;

internal sealed class FlowControlledBodyPump : BodyPumpBase<int>
{
    private readonly FlowController _flowController;
    private readonly HashSet<int> _windowBlockedStreams = new();
    private readonly List<int> _unblockedTemp = new();

    public FlowControlledBodyPump(
        IBodyDrainTarget<int> target,
        FlowController flowController,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts)
        : base(target, poolContext, connectionCts)
    {
        _flowController = flowController;
    }

    public void OnWindowUpdate(int streamId)
    {
        if (streamId == 0)
        {
            // Connection-level update: re-evaluate all blocked streams and unblock eligible ones.
            _unblockedTemp.Clear();
            foreach (var blocked in _windowBlockedStreams)
            {
                var window = Math.Min(
                    _flowController.GetStreamSendWindow(blocked),
                    _flowController.ConnectionSendWindow);
                if (window >= ComputeMinReadSize())
                {
                    _unblockedTemp.Add(blocked);
                }
            }

            foreach (var id in _unblockedTemp)
            {
                _windowBlockedStreams.Remove(id);
                EnqueueStream(id);
            }
        }
        else if (_windowBlockedStreams.Remove(streamId))
        {
            EnqueueStream(streamId);
        }

        // Always inject credits when active streams exist. The pump can reach zero credits
        // legitimately: the initial burst consumes bootstrap credits, all streams become
        // window-blocked, and no further OnOutboundFlushed calls replenish credits because
        // no data is being pushed. When a WINDOW_UPDATE subsequently unblocks streams (or
        // streams are already in the ready queue from a prior re-enqueue), the pump must be
        // able to read them. Without this unconditional boost the pump deadlocks — streams
        // sit in the ready queue with available window but zero credits to drive reads,
        // eventually tripping the data-rate monitor which RST_STREAMs the connection.
        if (GetActiveStreamCount() > 0)
        {
            for (var i = 0; i < 16; i++)
            {
                AddCredit();
            }
        }
    }

    public void Cleanup() => CancelAll();

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
        slot.ReservedWindow = reserve;
    }

    protected override void AfterRead(int streamId, BodyDrainSlot<int> slot, int bytesRead)
    {
        var refund = slot.ReservedWindow - bytesRead;
        if (refund > 0)
        {
            _flowController.Refund(streamId, refund);
        }

        slot.ReservedWindow = 0;
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
