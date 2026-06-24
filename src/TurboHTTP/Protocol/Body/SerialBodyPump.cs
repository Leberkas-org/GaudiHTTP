using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

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

    public void Register(Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        base.Register(0, bodyStream, contentLength, requestCt);
        for (var i = 0; i < _initialCredits; i++)
        {
            AddCredit();
        }
    }

    public void Cleanup() => CancelAll();
}
