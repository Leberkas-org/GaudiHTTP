using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.Server;

public sealed class TurboHttpContext
{
    public HttpRequestMessage Request { get; }
    public HttpResponseMessage Response { get; set; }

    public Source<ReadOnlyMemory<byte>, NotUsed> RequestBodySource { get; }
    public Source<ReadOnlyMemory<byte>, NotUsed> ResponseBodySource { get; set; }

    public RouteValueDictionary RouteValues { get; }

    public TurboConnectionInfo Connection { get; }
    public CancellationToken RequestAborted { get; }

    public IMaterializer? Materializer { get; internal set; }
    public IServiceProvider? RequestServices { get; internal set; }
    public IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public TurboHttpContext(
        HttpRequestMessage request,
        TurboConnectionInfo connection,
        Source<ReadOnlyMemory<byte>, NotUsed> requestBodySource,
        CancellationToken requestAborted)
    {
        Request = request;
        Response = new HttpResponseMessage(HttpStatusCode.OK);
        RequestBodySource = requestBodySource;
        ResponseBodySource = Source.Empty<ReadOnlyMemory<byte>>();
        RouteValues = new RouteValueDictionary();
        Connection = connection;
        RequestAborted = requestAborted;
    }
}
