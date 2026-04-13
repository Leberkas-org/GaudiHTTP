using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Internal;

namespace TurboHTTP.Streams;

public sealed class Http30Engine : IHttpProtocolEngine
{
    /// <summary>
    /// Maximum bytes accumulated before flushing a batch to the transport.
    /// Matches the HTTP/2 engine limit — QUIC flow-control already constrains
    /// per-stream throughput, so 64 KiB covers typical burst sizes.
    /// </summary>
    private const long MaxBatchWeight = 65_536;

    private readonly Http3ConnectionConfig _config;

    public Http30Engine() : this(new Http3ConnectionConfig())
    {
    }

    public Http30Engine(int maxTableCapacity, TimeSpan idleTimeout)
        : this(new Http3ConnectionConfig(
            QpackMaxTableCapacity: maxTableCapacity,
            IdleTimeout: idleTimeout))
    {
    }

    public Http30Engine(int maxTableCapacity, Http3ConnectionConfig config)
        : this(new Http3ConnectionConfig(
            MaxFieldSectionSize: config.MaxFieldSectionSize,
            QpackMaxTableCapacity: maxTableCapacity,
            QpackBlockedStreams: config.QpackBlockedStreams,
            IdleTimeout: config.IdleTimeout,
            MaxReconnectAttempts: config.MaxReconnectAttempts,
            AllowServerPush: config.AllowServerPush,
            AllowEarlyData: config.AllowEarlyData,
            AllowConnectionMigration: config.AllowConnectionMigration))
    {
    }

    public Http30Engine(Http3ConnectionConfig config)
    {
        _config = config;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http30ConnectionStage(_config));
            var batchFlow = b.Add(new NetworkBufferBatchStage(MaxBatchWeight));

            b.From(connection.OutNetwork).Via(batchFlow);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                connection.InApp,
                batchFlow.Outlet,
                connection.InServer,
                connection.OutResponse);
        }));
    }
}
