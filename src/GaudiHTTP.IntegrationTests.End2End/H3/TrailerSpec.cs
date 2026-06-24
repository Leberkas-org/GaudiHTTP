using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H3;

[Collection("H3")]
public sealed class TrailerSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version30;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/with-trailers", async (HttpContext ctx) =>
        {
            var trailersFeature = ctx.Features.Get<IHttpResponseTrailersFeature>();
            if (trailersFeature is not null)
            {
                trailersFeature.Trailers["x-checksum"] = "abc123";
                trailersFeature.Trailers["x-request-id"] = "req-42";
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("Hello with trailers", ctx.RequestAborted);
        });

        app.MapGet("/no-trailers", async (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("Hello without trailers", ctx.RequestAborted);
        });
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Server_should_send_trailing_headers_frame()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/with-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("Hello with trailers", body);

        Assert.Equal("abc123", response.TrailingHeaders.GetValues("x-checksum").Single());
        Assert.Equal("req-42", response.TrailingHeaders.GetValues("x-request-id").Single());
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Server_should_send_no_trailers_when_feature_empty()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/no-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Empty(response.TrailingHeaders);
    }
}
