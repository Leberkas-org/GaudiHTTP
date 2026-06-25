using Akka.Actor;
using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class MultiplexedBodyPump : BodyPumpBase<long>
{
    public MultiplexedBodyPump(
        IBodyDrainTarget<long> target,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts)
        : base(target, poolContext, connectionCts)
    {
        _yieldBetweenDrainPasses = true;
    }

    protected override void ScheduleContinuation()
    {
        Target.PipeToTarget.Tell(ContinueDrain.Instance, ActorRefs.NoSender);
    }

    public void Cleanup() => CancelAll();
}
