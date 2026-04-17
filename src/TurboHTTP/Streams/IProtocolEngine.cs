using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams;

internal interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        IOutputItem,
        IInputItem,
        HttpResponseMessage,
        NotUsed> CreateFlow();
}