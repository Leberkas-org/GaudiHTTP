using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.Shared;
using TurboHTTP.Server;
using Xunit;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class SseServerSpec : Xunit.IAsyncLifetime
{
    private TurboServerFixture? _fixture;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        _fixture = new TurboServerFixture(app =>
        {
            app.MapTurboGet("/echo", () => Results.Ok("ok"));
            app.MapTurboGet("/text", () => Results.Ok("hello world"));
            app.MapTurboGet("/events", () =>
            {
                var source = Source.From(["event1", "event2"]);
                return TurboStreamResults.EventStream(source);
            });
        });

        await _fixture.InitializeAsync();
        _client = new HttpClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_basic_request()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_fixture!.HttpPort}/echo"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_text_request()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_fixture!.HttpPort}/text"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("hello world", body);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_correct_content_type()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_fixture!.HttpPort}/text"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("application/json", response.Content.Headers.ContentType.MediaType ?? "");
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_fixture!.HttpPort}/nonexistent"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_stream_sse_events()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_fixture!.HttpPort}/events"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("data: event1\n\n", body);
        Assert.Contains("data: event2\n\n", body);
    }
}
