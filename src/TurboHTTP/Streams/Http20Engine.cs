using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Internal;

namespace TurboHTTP.Streams;

public class Http20Engine : IHttpProtocolEngine
{
    /// <summary>
    /// Maximum bytes accumulated before flushing a batch to the transport.
    /// 32 KiB covers a burst of ~64 HEADERS frames (≈500 B each at CL=16–64)
    /// or two full 16 KiB DATA frames, without excessive copy overhead.
    /// Smaller than the H1.1 limit (64 KiB) because H2 per-stream flow-control
    /// already limits how much data any single stream can push at once.
    /// </summary>
    internal const long MaxBatchWeight = 32_768;

    public Http20Engine() : this(1_048_576, int.MaxValue)
    {
    }

    public Http20Engine(int initialWindowSize, int maxConcurrentStreams)
    {
        InitialWindowSize = initialWindowSize;
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    /// <summary>
    /// The configured initial MAX_CONCURRENT_STREAMS limit.
    /// This value is passed to the underlying <see cref="Http20ConnectionStage"/>
    /// and will be updated at runtime when the server sends a SETTINGS frame
    /// with <see cref="Protocol.Http2.SettingsParameter.MaxConcurrentStreams"/>.
    /// </summary>
    public int MaxConcurrentStreams { get; }

    /// <summary>The configured initial receive flow-control window size in bytes.</summary>
    internal int InitialWindowSize { get; }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http20ConnectionStage(
                new Http2ConnectionConfig(InitialWindowSize, MaxConcurrentStreams)));

            // Coalesce consecutive NetworkBuffer frames (HEADERS, DATA, WINDOW_UPDATE, …)
            // from the H2 connection stage into fewer, larger writes — reducing socket
            // syscall count under concurrent multiplexed streams.  Control items are
            // flushed through immediately so H2 frame ordering is preserved.
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