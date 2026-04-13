using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Internal;

namespace TurboHTTP.Streams;

/// <summary>
/// Builds the protocol engine core: <see cref="GroupByExtensions.GroupByRequestEndpoint{T,TMat}"/>
/// groups by <see cref="RequestEndpoint"/> (scheme, host, port, version), then each substream
/// uses a <see cref="EndpointDispatchStage"/> that lazily materializes the correct version-specific
/// connection flow based on the first element — no Partition/Merge overhead.
/// </summary>
internal static class ProtocolCoreBuilder
{
    internal static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> Build(
        TurboClientOptions clientOptions,
        TransportRegistry transports)
    {
        // Higher buffer sizes reduce backpressure signaling frequency, which lowers
        // per-element overhead in high-concurrency scenarios. The initialSize handles
        // typical burst sizes (HTTP/2 multiplexed streams); maxSize accommodates
        // sustained throughput peaks without excessive memory.
        var highThroughputBuffer = Attributes.CreateInputBuffer(64, 256);

        var maxConnsH1 = clientOptions.Http1.MaxConnectionsPerServer;
        var maxConnsH2 = clientOptions.Http2.MaxConnectionsPerServer;
        var maxConcurrentH2Streams = clientOptions.Http2.MaxConcurrentStreams;
        var maxConnsH3 = clientOptions.Http3.MaxConnectionsPerServer;

        var endpointDispatch = Flow.FromGraph(new EndpointDispatchStage(CreateFlowForEndpoint))
            .WithAttributes(highThroughputBuffer);

        var core = (Flow<HttpRequestMessage, HttpResponseMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByRequestEndpoint(RequestEndpoint.FromRequest, maxSubstreams: clientOptions.MaxEndpointSubstreams,
                    maxSubstreamsPerKey: MaxSubstreamsPerKey,
                    maxConcurrencyPerSlot: MaxConcurrencyPerSlot)
                .ViaSubFlow(endpointDispatch)
                .MergeSubstreams();

        return core.WithAttributes(highThroughputBuffer);

        int MaxConcurrencyPerSlot(RequestEndpoint endpoint)
            => GetMaxConcurrencyPerSlot(endpoint, maxConcurrentH2Streams, clientOptions.Http1.MaxPipelineDepth);

        int MaxSubstreamsPerKey(RequestEndpoint endpoint)
            => GetMaxSubstreamsPerKey(endpoint, maxConnsH1, maxConnsH2, maxConnsH3);

        // Endpoint-specific flow factory — called once per substream on first element.
        // Since GroupByRequestEndpoint already groups by endpoint, each substream
        // contains a single endpoint — no Partition/Merge needed.
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlowForEndpoint(RequestEndpoint endpoint)
        {
            var version = endpoint.Version;
            IHttpProtocolEngine engine = version switch
            {
                { Major: 1, Minor: 0 } => new Http10Engine(),
                { Major: 1, Minor: 1 } => new Http11Engine(clientOptions.Http1.MaxPipelineDepth),
                { Major: 2, Minor: 0 } => new Http20Engine(clientOptions.Http2.InitialWindowSize, maxConcurrentH2Streams),
                { Major: 3, Minor: 0 } => new Http30Engine(
                    clientOptions.Http3.QpackMaxTableCapacity,
                    new Protocol.Http3.Http3ConnectionConfig(
                        MaxFieldSectionSize: clientOptions.Http3.MaxFieldSectionSize,
                        QpackMaxTableCapacity: clientOptions.Http3.QpackMaxTableCapacity,
                        QpackBlockedStreams: clientOptions.Http3.QpackBlockedStreams,
                        IdleTimeout: clientOptions.Http3.IdleTimeout,
                        MaxReconnectAttempts: clientOptions.Http3.MaxReconnectAttempts,
                        AllowEarlyData: clientOptions.Http3.AllowEarlyData,
                        AllowConnectionMigration: clientOptions.Http3.AllowConnectionMigration)),
                _ => throw new ArgumentOutOfRangeException(nameof(version), version, $"Unsupported HTTP version: {version}")
            };

            // Async boundary on the joined flow: the full engine+transport sub-graph
            // runs in its own sub-actor (separate from GroupBy/EndpointDispatch).
            return engine.CreateFlow().Join(transports.Get(version));
        }
    }

    internal static int GetMaxConcurrencyPerSlot(RequestEndpoint endpoint, int maxConcurrentH2Streams, int maxPipelineDepth)
        => endpoint.Version.Major == 3 ? int.MaxValue      // QUIC handles stream limits at transport level
         : endpoint.Version.Major == 2 ? maxConcurrentH2Streams
         : endpoint.Version is { Major: 1, Minor: 0 } ? 1  // HTTP/1.0 no pipelining
         : maxPipelineDepth;                                 // HTTP/1.1 pre-fill slots

    internal static int GetMaxSubstreamsPerKey(RequestEndpoint endpoint, int maxConnsH1, int maxConnsH2, int maxConnsH3)
        => endpoint.Version.Major == 3 ? maxConnsH3
         : endpoint.Version.Major == 2 ? maxConnsH2
         : maxConnsH1;
}
