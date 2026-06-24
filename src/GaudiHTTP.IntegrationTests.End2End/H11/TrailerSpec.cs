using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.IntegrationTests.End2End.Shared;

namespace GaudiHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class TrailerSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

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

        app.MapGet("/prohibited-trailers", async (HttpContext ctx) =>
        {
            var trailersFeature = ctx.Features.Get<IHttpResponseTrailersFeature>();
            if (trailersFeature is not null)
            {
                trailersFeature.Trailers["x-valid"] = "yes";
                trailersFeature.Trailers["transfer-encoding"] = "chunked";
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("Hello", ctx.RequestAborted);
        });
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public async Task Server_should_send_trailers_in_chunked_response()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/with-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("Hello with trailers", body);

        Assert.True(response.TrailingHeaders.Contains("x-checksum"),
            "Expected x-checksum trailing header");
        Assert.Equal("abc123", response.TrailingHeaders.GetValues("x-checksum").Single());
        Assert.Equal("req-42", response.TrailingHeaders.GetValues("x-request-id").Single());
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public async Task Server_should_auto_generate_trailer_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/with-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Trailer"),
            "Expected auto-generated Trailer response header");
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public async Task Server_should_send_no_trailers_when_feature_empty()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/no-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("Hello without trailers", body);
        Assert.Empty(response.TrailingHeaders);
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public async Task Server_should_filter_prohibited_trailer_fields()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/prohibited-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.True(response.TrailingHeaders.Contains("x-valid"));
        Assert.False(response.TrailingHeaders.Contains("transfer-encoding"));
    }
}
