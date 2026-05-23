using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using TurboHTTP.Features.Sse;

namespace TurboHTTP.Tests.Features.Sse;

public sealed class SseFormatterFlowSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public SseFormatterFlowSpec() : base(ActorSystem.Create("test"))
    {
        _materializer = Sys.Materializer();
    }

    private async Task<string> FormatSingle(ServerSentEvent evt)
    {
        var result = await Source.Single(evt)
            .Via(SseFormatterFlow.Instance)
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        return string.Concat(result.Select(c => Encoding.UTF8.GetString(c.Span)));
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_format_simple_data_event()
    {
        var output = await FormatSingle(new ServerSentEvent("hello"));

        Assert.Equal("data: hello\n\n", output);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_format_event_with_all_fields()
    {
        var evt = new ServerSentEvent(
            Data: "payload",
            EventType: "update",
            Id: "42",
            Retry: TimeSpan.FromMilliseconds(3000));

        var output = await FormatSingle(evt);

        Assert.Equal("event: update\nid: 42\nretry: 3000\ndata: payload\n\n", output);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_format_multiline_data()
    {
        var evt = new ServerSentEvent("line1\nline2\nline3");

        var output = await FormatSingle(evt);

        Assert.Equal("data: line1\ndata: line2\ndata: line3\n\n", output);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_format_event_with_event_type_only()
    {
        var evt = new ServerSentEvent("hello", EventType: "ping");

        var output = await FormatSingle(evt);

        Assert.Equal("event: ping\ndata: hello\n\n", output);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_format_event_with_id_only()
    {
        var evt = new ServerSentEvent("hello", Id: "99");

        var output = await FormatSingle(evt);

        Assert.Equal("id: 99\ndata: hello\n\n", output);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_format_multiple_events()
    {
        var result = await Source.From([
                new ServerSentEvent("first"),
                new ServerSentEvent("second")
            ])
            .Via(SseFormatterFlow.Instance)
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), _materializer);

        var output = string.Concat(result.Select(c => Encoding.UTF8.GetString(c.Span)));

        Assert.Equal("data: first\n\ndata: second\n\n", output);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_format_empty_data()
    {
        var output = await FormatSingle(new ServerSentEvent(""));

        Assert.Equal("data: \n\n", output);
    }

    [Fact(Timeout = 5000)]
    public async Task Flow_should_roundtrip_through_parser()
    {
        var original = new ServerSentEvent(
            Data: "line1\nline2",
            EventType: "update",
            Id: "7",
            Retry: TimeSpan.FromMilliseconds(5000));

        var parsed = await Source.Single(original)
            .Via(SseFormatterFlow.Instance)
            .Via(SseParserFlow.Instance)
            .RunWith(Sink.Seq<ServerSentEvent>(), _materializer);

        Assert.Single(parsed);
        Assert.Equal(original, parsed[0]);
    }
}
