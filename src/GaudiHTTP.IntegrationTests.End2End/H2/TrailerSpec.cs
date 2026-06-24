using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.IntegrationTests.End2End.Shared;

namespace GaudiHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class TrailerSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/with-trailers", async (HttpContext ctx) =>
        {
            var trailersFeature = ctx.Features.Get<IHttpResponseTrailersFeature>();
            if (trailersFeature is not null)
            {
                trailersFeature.Trailers["grpc-status"] = "0";
                trailersFeature.Trailers["grpc-message"] = "OK";
                trailersFeature.Trailers["x-checksum"] = "abc123";
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
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Server_should_send_trailing_headers_frame()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/with-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("Hello with trailers", body);

        Assert.Equal("0", response.TrailingHeaders.GetValues("grpc-status").Single());
        Assert.Equal("OK", response.TrailingHeaders.GetValues("grpc-message").Single());
        Assert.Equal("abc123", response.TrailingHeaders.GetValues("x-checksum").Single());
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Client_should_receive_grpc_status_trailers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/with-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.True(response.TrailingHeaders.Contains("grpc-status"));
        Assert.True(response.TrailingHeaders.Contains("grpc-message"));
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Server_should_send_no_trailers_when_feature_empty()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/no-trailers");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Empty(response.TrailingHeaders);
    }
}
