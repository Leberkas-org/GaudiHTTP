using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// Body-suppressed responses (1xx/204/304 and responses to HEAD) emit headers only and never run
/// a body drain, so the state machine must recycle the pooled feature collection itself by
/// signalling OnResponseBodyComplete. Previously the suppress-body path returned early without it,
/// dropping the collection (a missed-recycling / GC-pressure leak).
/// </summary>
public sealed class Http11ServerResponseRecyclingSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
        => new(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

    private static void SendRequest(Http11ServerStateMachine sm, string method = "GET")
    {
        var raw = $"{method} / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(TransportData.Rent(buffer));
    }

    private static IFeatureCollection ResponseFeatures(int statusCode, string requestMethod = "GET")
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature { Method = requestMethod });
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = statusCode });
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());
        return features;
    }

    [Theory]
    [Trait("RFC", "RFC9110-9.3.2")]
    [InlineData(204)]
    [InlineData(304)]
    public void Body_suppressed_status_response_should_recycle_feature_collection(int statusCode)
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var features = ResponseFeatures(statusCode);
        sm.OnResponse(features);

        Assert.Contains(features, ops.ResponseBodyCompletions);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.3.2")]
    public void Head_response_should_recycle_feature_collection()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm, "HEAD");

        var features = ResponseFeatures(statusCode: 200, requestMethod: "HEAD");
        sm.OnResponse(features);

        Assert.Contains(features, ops.ResponseBodyCompletions);
    }
}
