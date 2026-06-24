using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

internal sealed class SerialBodyPump : BodyPumpBase<int>
{
    public SerialBodyPump(
        IBodyDrainTarget<int> target,
        ConnectionPoolContext poolContext,
        CancellationTokenSource connectionCts)
        : base(target, poolContext, connectionCts)
    {
    }

    public void Register(Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        base.Register(0, bodyStream, contentLength, requestCt);
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
