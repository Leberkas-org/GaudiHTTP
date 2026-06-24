using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class SerialBodyPump : BodyPumpBase<int>
{
    private readonly int _initialCredits;

    public SerialBodyPump(
        IBodyDrainTarget<int> target,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts,
        int initialCredits = 2)
        : base(target, poolContext, connectionCts)
    {
        _initialCredits = initialCredits;
    }

    public void Register(Stream bodyStream, CancellationToken requestCt)
    {
        base.Register(0, bodyStream, requestCt, _initialCredits);
    }

    public void Cleanup() => CancelAll();
}
