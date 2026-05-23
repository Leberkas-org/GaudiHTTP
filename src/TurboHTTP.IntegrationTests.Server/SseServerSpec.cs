using System.Net;
using System.Net.Http.Headers;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHTTP.IntegrationTests.Shared;
using TurboHTTP.Server;
using Xunit;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class SseServerSpec : Xunit.IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;
    private int _port;

    public async ValueTask InitializeAsync()
    {
        _port = GetFreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.Urls.Clear();
        _app.Urls.Add($"http://127.0.0.1:{_port}");

        _app.MapGet("/echo", () => Results.Ok("ok"));
        _app.MapGet("/text", () => Results.Text("hello world"));
        _app.MapGet("/nonexistent404", () => Results.NotFound());

        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_basic_request()
    {
        var response = await _client!.GetAsync("/echo", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_text_request()
    {
        var response = await _client!.GetAsync("/text", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await _client!.GetAsync("/unregistered", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_correct_content_type()
    {
        var response = await _client!.GetAsync("/text", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/plain", response.Content.Headers.ContentType?.ToString() ?? "");
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
