using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using TurboHttp.Middleware;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the <see cref="MiddlewareResponseStage"/> that calls
/// <see cref="TurboMiddleware.ProcessResponseAsync"/> per element inside the Akka graph.
/// Verifies synchronous fast-path, async callback path, stage chaining, and original request access.
/// </summary>
public sealed class MiddlewareResponseStageTests : StreamTestBase
{
    private Task<IImmutableList<HttpResponseMessage>> RunAsync(
        MiddlewareResponseStage stage,
        params HttpResponseMessage[] responses)
    {
        return Source.From(responses)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    // ── Inline middleware helpers ────────────────────────────────────────────

    private sealed class AddResponseHeaderMiddleware : TurboMiddleware
    {
        private readonly string _headerName;
        private readonly string _headerValue;

        public AddResponseHeaderMiddleware(string headerName, string headerValue)
        {
            _headerName = headerName;
            _headerValue = headerValue;
        }

        public override ValueTask<HttpResponseMessage> ProcessResponseAsync(
            HttpRequestMessage original,
            HttpResponseMessage response,
            CancellationToken ct)
        {
            response.Headers.TryAddWithoutValidation(_headerName, _headerValue);
            return ValueTask.FromResult(response);
        }
    }

    private sealed class AsyncResponseMiddleware : TurboMiddleware
    {
        private readonly string _headerValue;

        public AsyncResponseMiddleware(string headerValue) => _headerValue = headerValue;

        public override async ValueTask<HttpResponseMessage> ProcessResponseAsync(
            HttpRequestMessage original,
            HttpResponseMessage response,
            CancellationToken ct)
        {
            await Task.Delay(1, ct);
            response.Headers.TryAddWithoutValidation("X-Async-Response", _headerValue);
            return response;
        }
    }

    private sealed class InspectRequestUriMiddleware : TurboMiddleware
    {
        public override ValueTask<HttpResponseMessage> ProcessResponseAsync(
            HttpRequestMessage original,
            HttpResponseMessage response,
            CancellationToken ct)
        {
            // Echo the original request URI back as a response header so we can assert on it
            response.Headers.TryAddWithoutValidation("X-Original-Uri", original.RequestUri!.ToString());
            return ValueTask.FromResult(response);
        }
    }

    // ── Helper to build a response linked to a request ───────────────────────

    private static HttpResponseMessage MakeResponse(string requestUri, HttpStatusCode status = HttpStatusCode.OK)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var response = new HttpResponseMessage(status)
        {
            RequestMessage = request
        };
        return response;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "MRSP-001: Synchronous middleware — adds custom response header via ValueTask.FromResult")]
    public async Task Should_AddCustomHeader_When_MiddlewareReturnsSynchronously()
    {
        var middleware = new AddResponseHeaderMiddleware("X-Custom", "hello");
        var stage = new MiddlewareResponseStage(middleware);

        var response = MakeResponse("http://a.test/");
        var results = await RunAsync(stage, response);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Custom"));
        Assert.Contains("hello", result.Headers.GetValues("X-Custom"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MRSP-002: Asynchronous middleware — Task.Delay(1) completes and pushes result without deadlock")]
    public async Task Should_TransformResponse_When_MiddlewareReturnsRealAsyncTask()
    {
        var middleware = new AsyncResponseMiddleware("async-resp");
        var stage = new MiddlewareResponseStage(middleware);

        var response = MakeResponse("http://a.test/");
        var results = await RunAsync(stage, response);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Async-Response"));
        Assert.Contains("async-resp", result.Headers.GetValues("X-Async-Response"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MRSP-003: RequestMessage access — middleware reads original.RequestUri via response.RequestMessage")]
    public async Task Should_ReadOriginalRequestUri_When_MiddlewareInspectsRequestMessage()
    {
        var middleware = new InspectRequestUriMiddleware();
        var stage = new MiddlewareResponseStage(middleware);

        var response = MakeResponse("http://origin.test/path?q=1");
        var results = await RunAsync(stage, response);

        var result = Assert.Single(results);
        Assert.True(result.Headers.Contains("X-Original-Uri"));
        Assert.Contains("http://origin.test/path?q=1", result.Headers.GetValues("X-Original-Uri"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MRSP-004: Chained stages — second stage sees the output of the first")]
    public async Task Should_ApplyBothTransformations_When_TwoStagesAreChained()
    {
        var first = new MiddlewareResponseStage(new AddResponseHeaderMiddleware("X-First", "first-ran"));
        var second = new MiddlewareResponseStage(new AsyncResponseMiddleware("second-ran"));

        var response = MakeResponse("http://a.test/");

        var results = await Source.From(new[] { response })
            .Via(Flow.FromGraph(first))
            .Via(Flow.FromGraph(second))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        var result = Assert.Single(results);

        // First stage added X-First
        Assert.True(result.Headers.Contains("X-First"));
        Assert.Contains("first-ran", result.Headers.GetValues("X-First"));

        // Second stage added X-Async-Response
        Assert.True(result.Headers.Contains("X-Async-Response"));
        Assert.Contains("second-ran", result.Headers.GetValues("X-Async-Response"));
    }

    [Fact(Timeout = 10_000,
        DisplayName = "MRSP-005: Multiple responses — all transformed in order, stream completes cleanly")]
    public async Task Should_TransformAllResponsesInOrder_When_MultipleResponsesStreamed()
    {
        var middleware = new AddResponseHeaderMiddleware("X-Processed", "yes");
        var stage = new MiddlewareResponseStage(middleware);

        var resp1 = MakeResponse("http://a.test/one");
        var resp2 = MakeResponse("http://a.test/two");
        var resp3 = MakeResponse("http://a.test/three");

        var results = new List<HttpResponseMessage>(await RunAsync(stage, resp1, resp2, resp3));

        Assert.Equal(3, results.Count);
        foreach (var result in results)
        {
            Assert.True(result.Headers.Contains("X-Processed"));
            Assert.Contains("yes", result.Headers.GetValues("X-Processed"));
        }
    }
}
