using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

internal sealed class MultiplexedBodyPump : BodyPumpBase<long>
{
    public MultiplexedBodyPump(
        IBodyDrainTarget<long> target,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts)
        : base(target, poolContext, connectionCts)
    {
    }

    public new void Register(long streamId, Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        base.Register(streamId, bodyStream, contentLength, requestCt);
        // Bootstrap with sufficient credits to drain synchronous streams
        // Budget threshold for single stream is min(budget/2, 2) ≈ 1-15 credits needed
        // Add extra credits to handle typical small body (data + EOF) without additional calls
        for (var i = 0; i < 16; i++)
        {
            AddCredit();
        }
    }

    public void Cleanup() => CancelAll();
}
