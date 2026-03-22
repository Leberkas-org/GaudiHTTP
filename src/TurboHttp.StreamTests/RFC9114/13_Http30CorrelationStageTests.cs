using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 stream correlation stage per RFC 9114.
/// Verifies that responses are matched to their originating requests using QUIC stream IDs
/// and that RequestMessage is set correctly.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30CorrelationStage"/>.
/// RFC 9114 §4.1: HTTP/3 request-response exchange on bidirectional streams with stream-ID-based correlation.
/// </remarks>
public sealed class Http30CorrelationStageTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http30CorrelationStage with the given sources and returns collected responses.
    /// In0 = (HttpRequestMessage, streamId), In1 = (HttpResponseMessage, streamId), Out = HttpResponseMessage.
    /// </summary>
    private async Task<List<HttpResponseMessage>> RunStageAsync(
        Source<(HttpRequestMessage, long), NotUsed> requestSource,
        Source<(HttpResponseMessage, long), NotUsed> responseSource,
        TimeSpan? timeout = null)
    {
        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http30CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        var result = await task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        return result.ToList();
    }

    private static HttpRequestMessage MakeRequest(int index = 0)
        => new(HttpMethod.Get, $"http://example.com/{index}");

    private static HttpResponseMessage OkResponse()
        => new(HttpStatusCode.OK);

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-001: Single (Request,streamId=0) + (Response,streamId=0) → correctly correlated")]
    public async Task Should_Correlate_Single_Request_And_Response()
    {
        var request = MakeRequest();
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single((request, 0L)),
            Source.Single((response, 0L)));

        Assert.Single(results);
        Assert.Same(request, results[0].RequestMessage);
        Assert.Same(response, results[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-002: 3 requests (IDs 0,4,8) + 3 responses (IDs 8,0,4) → out-of-order correlation")]
    public async Task Should_Correlate_Out_Of_Order_Responses()
    {
        var req0 = MakeRequest(0);
        var req4 = MakeRequest(4);
        var req8 = MakeRequest(8);

        var res0 = OkResponse();
        var res4 = OkResponse();
        var res8 = OkResponse();

        var requests = new[] { (req0, 0L), (req4, 4L), (req8, 8L) };
        // Responses arrive in reverse stream-ID order
        var responses = new[] { (res8, 8L), (res0, 0L), (res4, 4L) };

        var results = await RunStageAsync(
            Source.From(requests),
            Source.From(responses));

        Assert.Equal(3, results.Count);

        // Each response must carry its matching request via RequestMessage
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req0) && ReferenceEquals(r, res0));
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req4) && ReferenceEquals(r, res4));
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req8) && ReferenceEquals(r, res8));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-003: Response stream ID with no matching request → stays in queue")]
    public async Task Should_Keep_Unmatched_Response_In_Queue()
    {
        var req0 = MakeRequest(0);
        var res99 = OkResponse(); // stream ID 99 — no request was sent on this stream

        var requestSource = Source.Single((req0, 0L))
            .Concat(Source.Never<(HttpRequestMessage, long)>());
        var responseSource = Source.Single((res99, 99L))
            .Concat(Source.Never<(HttpResponseMessage, long)>());

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http30CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete: the unmatched response keeps _waiting non-empty
        var completedEarly = task.WaitAsync(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<TimeoutException>(() => completedEarly);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-004: Reference equality: response.RequestMessage is exactly the sent object")]
    public async Task Should_Set_RequestMessage_As_Exact_Same_Reference()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("payload")
        };
        var response = OkResponse();

        var results = await RunStageAsync(
            Source.Single((request, 4L)),
            Source.Single((response, 4L)));

        Assert.True(ReferenceEquals(request, results[0].RequestMessage),
            "response.RequestMessage must be the exact same object reference as the original request.");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-005: 10 interleaved requests/responses with QUIC stream IDs → all correctly matched")]
    public async Task Should_Match_All_When_Ten_Interleaved_Requests()
    {
        const int count = 10;

        // QUIC client-initiated bidirectional stream IDs: 0, 4, 8, 12, …
        var requests = Enumerable.Range(0, count)
            .Select(i => (Msg: MakeRequest(i), StreamId: (long)(i * 4)))
            .ToArray();

        // Shuffle responses relative to requests (reverse order)
        var responses = requests
            .Select(r => (Msg: OkResponse(), r.StreamId))
            .Reverse()
            .ToArray();

        var requestSource = Source.From(requests.Select(r => (r.Msg, r.StreamId)));
        var responseSource = Source.From(responses.Select(r => (r.Msg, r.StreamId)));

        var results = await RunStageAsync(requestSource, responseSource);

        Assert.Equal(count, results.Count);

        // Build lookup by stream-id → original request
        var requestById = requests.ToDictionary(r => r.StreamId, r => r.Msg);

        foreach (var result in results)
        {
            var matched = requestById.Values.FirstOrDefault(r => ReferenceEquals(r, result.RequestMessage));
            Assert.NotNull(matched);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-006: Stage stays alive after correlation when upstreams remain open")]
    public async Task Should_StayAlive_When_Dicts_Empty_But_Upstream_Open()
    {
        var request = MakeRequest();
        var response = OkResponse();

        var requestSource = Source.Single((request, 0L))
            .Concat(Source.Never<(HttpRequestMessage, long)>());
        var responseSource = Source.Single((response, 0L))
            .Concat(Source.Never<(HttpResponseMessage, long)>());

        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http30CorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);

            b.From(reqSrc).To(corr.In0);
            b.From(resSrc).To(corr.In1);
            b.From(corr.Out).To(s);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);

        // Stage must NOT complete even though dictionaries are empty — upstreams are still open.
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-4.1-30CR-007: Request(0), Response(4), Request(4) → correlation immediately on match")]
    public async Task Should_Correlate_Immediately_On_Match_When_Interleaved_Push()
    {
        var req0 = MakeRequest(0);
        var req4 = MakeRequest(4);
        var res4 = OkResponse();
        var res0 = OkResponse();

        var requestSource = Source.From([(req0, 0L), (req4, 4L)]);
        var responseSource = Source.From([(res4, 4L), (res0, 0L)]);

        var results = await RunStageAsync(requestSource, responseSource);

        Assert.Equal(2, results.Count);

        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req0) && ReferenceEquals(r, res0));
        Assert.Contains(results, r => ReferenceEquals(r.RequestMessage, req4) && ReferenceEquals(r, res4));
    }
}
