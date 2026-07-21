using System.Text;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// Body-suppressed responses (1xx/204/304 and responses to HEAD) emit headers only and never run
/// a body drain, so the state machine must recycle the pooled feature collection itself by
/// signalling OnResponseBodyComplete. Previously the suppress-body path returned early without it,
/// dropping the collection (a missed-recycling / GC-pressure leak).
/// </summary>
public sealed class Http11ServerResponseRecyclingSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
        => new(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

    private static void SendRequest(Http11ServerStateMachine sm, FakeServerOps ops, string method = "GET")
    {
        var raw = $"{method} / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        sm.ConnectTransport(Encoding.ASCII.GetBytes(raw), ops);
    }

    private static IFeatureCollection ResponseFeatures(int statusCode, string requestMethod = "GET")
    {
        var features = ServerTestContext.CreateResponse(statusCode);
        features.Get<IHttpRequestFeature>()!.Method = requestMethod;
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
        SendRequest(sm, ops);

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
        SendRequest(sm, ops, "HEAD");

        var features = ResponseFeatures(statusCode: 200, requestMethod: "HEAD");
        sm.OnResponse(features);

        Assert.Contains(features, ops.ResponseBodyCompletions);
    }
}
