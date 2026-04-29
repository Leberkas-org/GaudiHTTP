using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Transport.Tcp;

public sealed class TcpConnectionStageSpec : StreamTestBase
{
    private sealed class StubPoolingStrategy : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => 6;
        public TimeSpan IdleTimeout => TimeSpan.FromSeconds(30);
        public TimeSpan ConnectionLifetime => TimeSpan.FromMinutes(5);
        public bool CanReuse(TransportOptions options) => true;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
        public PoolAction OnIdle(object lease) => PoolAction.Reuse;
        public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
        public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_complete_when_upstream_finishes_without_connection()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody, new StubPoolingStrategy());
        var flow = Flow.FromGraph(stage);

        var result = await Source.Empty<ITransportOutbound>()
            .Via(flow)
            .RunWith(Sink.Seq<ITransportInbound>(), Materializer);

        Assert.Empty(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_materialize_successfully()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody, new StubPoolingStrategy());
        var flow = Flow.FromGraph(stage);

        var result = await Source.Empty<ITransportOutbound>()
            .Via(flow)
            .ToMaterialized(Sink.Seq<ITransportInbound>(), Keep.Both)
            .Run(Materializer);

        Assert.Empty(result.Item2.Result);
    }
}
