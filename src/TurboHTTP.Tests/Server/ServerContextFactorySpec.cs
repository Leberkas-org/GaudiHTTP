using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class ServerContextFactorySpec
{
    [Fact(Timeout = 5000)]
    public void Create_should_set_request_feature()
    {
        var requestFeature = new TurboHttpRequestFeature { Method = "POST", Path = "/api" };
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        Assert.Equal("POST", ctx.Request.Method);
        Assert.Equal("/api", ctx.Request.Path.Value);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_response_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var responseFeature = ctx.Features.Get<IHttpResponseFeature>();
        Assert.NotNull(responseFeature);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_request_body_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var bodyFeature = ctx.Features.Get<ITurboRequestBodyFeature>();
        Assert.NotNull(bodyFeature);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_body_detection_true_when_has_body()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: true);

        var detection = ctx.Features.Get<IHttpRequestBodyDetectionFeature>();
        Assert.NotNull(detection);
        Assert.True(detection.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_body_detection_false_when_no_body()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var detection = ctx.Features.Get<IHttpRequestBodyDetectionFeature>();
        Assert.NotNull(detection);
        Assert.False(detection.CanHaveBody);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_response_body_feature()
    {
        var requestFeature = new TurboHttpRequestFeature();
        var ctx = ServerContextFactory.Create(requestFeature, hasBody: false);

        var responseBodyFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
        Assert.NotNull(responseBodyFeature);

        var turboResponseBody = ctx.Features.Get<ITurboResponseBodyFeature>();
        Assert.NotNull(turboResponseBody);
    }
}
