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

    public void Cleanup() => CancelAll();
}
