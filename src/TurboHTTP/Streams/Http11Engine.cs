using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Decoding;
using TurboHTTP.Streams.Stages.Internal;

namespace TurboHTTP.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    internal const long MaxBatchWeight = 65_536;

    private readonly int _maxPipelineDepth;

    public Http11Engine(int maxPipelineDepth = 8)
    {
        _maxPipelineDepth = maxPipelineDepth;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http11ConnectionStage(_maxPipelineDepth));

            // NetworkBufferBatchStage coalesces consecutive NetworkBuffer items from the
            // encoder into fewer, larger writes — reducing Channel.WriteAsync + Socket.WriteAsync
            // syscalls under pipelining.  Unlike BatchWeighted, it correctly handles streams
            // that interleave NetworkBuffer data with control items (StreamAcquireItem,
            // ConnectionReuseItem): the accumulated buffer is flushed before the control item
            // is forwarded, so ordering is preserved and no bytes are ever dropped.
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