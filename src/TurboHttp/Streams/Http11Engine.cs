using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http11EncoderStage());
            var decoder = b.Add(new Http11DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            b.From(requestBCast.Out(0)).To(encoder.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.RequestIn);

            b.From(decoder.Outlet).To(correlation.ResponseIn);

            var signalCast = b.Add(Flow.Create<IControlItem>().Select(IOutputItem (x) => x));

            b.From(encoder.Outlet).To(signalMerge.In(0));
            b.From(correlation.OutletSignal).Via(signalCast).To(signalMerge.Preferred);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestBCast.In,
                signalMerge.Out,
                decoder.Inlet,
                correlation.Out);
        }));
    }
}