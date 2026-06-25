using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Protocol.Body;

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
        var unblocked = 0;

        if (streamId == 0)
        {
            var minRead = ComputeMinReadSize();
            if (_flowController.ConnectionSendWindow >= minRead)
            {
                _unblockedTemp.Clear();
                foreach (var blocked in _windowBlockedStreams)
                {
                    if (_flowController.GetStreamSendWindow(blocked) >= minRead)
                    {
                        _unblockedTemp.Add(blocked);
                    }
                }

                foreach (var id in _unblockedTemp)
                {
                    _windowBlockedStreams.Remove(id);
                    EnqueueStream(id);
                }

                unblocked = _unblockedTemp.Count;
            }
        }
        else if (_windowBlockedStreams.Remove(streamId))
        {
            EnqueueStream(streamId);
            unblocked = 1;
        }

        if (GetActiveStreamCount() > 0)
        {
            var boost = Math.Clamp(unblocked, 1, 16);
            for (var i = 0; i < boost; i++)
            {
                AddCreditWithoutEma();
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
