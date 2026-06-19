using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server.Context;

public sealed class TurboHttpResponseBodyFeatureSpec : TestKit
{
    [Fact(Timeout = 5000)]
    public async Task Stream_write_should_be_readable_from_GetResponseSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var feature = new TurboHttpResponseBodyFeature();
        var bodyBytes = "hello"u8.ToArray();

        await feature.Stream.WriteAsync(bodyBytes, ct);
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Sys.Materializer());

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal("hello", Encoding.UTF8.GetString(combined));
    }

    [Fact(Timeout = 5000)]
    public async Task PipeWriter_write_should_be_readable_from_GetResponseSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var feature = new TurboHttpResponseBodyFeature();
        var bodyBytes = "pipe-data"u8.ToArray();

        var memory = feature.Writer.GetMemory(bodyBytes.Length);
        bodyBytes.CopyTo(memory);
        feature.Writer.Advance(bodyBytes.Length);
        await feature.Writer.FlushAsync(ct);
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Sys.Materializer());

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal("pipe-data", Encoding.UTF8.GetString(combined));
    }

    [Fact(Timeout = 5000)]
    public async Task BodySink_should_receive_data_from_akka_source()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var chunk = new ReadOnlyMemory<byte>("akka-data"u8.ToArray());

        await Source.Single(chunk).RunWith(feature.BodySink, Sys.Materializer());
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Sys.Materializer());

        var combined = result.SelectMany(m => m.ToArray()).ToArray();
        Assert.Equal("akka-data", Encoding.UTF8.GetString(combined));
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseSource_should_return_empty_when_nothing_written()
    {
        var feature = new TurboHttpResponseBodyFeature();
        await feature.CompleteAsync();

        var result = await feature.GetResponseSource()
            .RunWith(Sink.Seq<ReadOnlyMemory<byte>>(), Sys.Materializer());

        Assert.Empty(result);
    }

    [Fact(Timeout = 5000)]
    public void Stream_should_return_same_instance()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var s1 = feature.Stream;
        var s2 = feature.Stream;
        Assert.Same(s1, s2);
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseStream_should_return_readable_stream()
    {
        var ct = TestContext.Current.CancellationToken;
        var feature = new TurboHttpResponseBodyFeature();
        var bodyBytes = "stream-data"u8.ToArray();

        await feature.Stream.WriteAsync(bodyBytes, ct);
        await feature.CompleteAsync();

        var responseStream = feature.GetResponseStream();
        var buffer = new byte[1024];
        var bytesRead = await responseStream.ReadAsync(buffer, ct);

        Assert.Equal("stream-data", Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    [Fact(Timeout = 5000)]
    public async Task GetResponseStream_should_return_empty_stream_when_nothing_written()
    {
        var ct = TestContext.Current.CancellationToken;
        var feature = new TurboHttpResponseBodyFeature();
        await feature.CompleteAsync();

        var responseStream = feature.GetResponseStream();
        var buffer = new byte[1024];
        var bytesRead = await responseStream.ReadAsync(buffer, ct);

        Assert.Equal(0, bytesRead);
    }

    [Fact(Timeout = 5000)]
    public async Task CompleteAsync_should_be_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var feature = new TurboHttpResponseBodyFeature();
        await feature.Stream.WriteAsync("data"u8.ToArray(), ct);
        await feature.CompleteAsync();
        await feature.CompleteAsync();

        var responseStream = feature.GetResponseStream();
        var buffer = new byte[1024];
        var bytesRead = await responseStream.ReadAsync(buffer, ct);

        Assert.Equal("data", Encoding.UTF8.GetString(buffer, 0, bytesRead));
    }

    [Fact(Timeout = 5000)]
    public async Task WhenHeadersReady_should_complete_after_StartAsync()
    {
        var feature = new TurboHttpResponseBodyFeature();

        Assert.False(feature.WhenHeadersReady.IsCompleted);

        await feature.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(feature.WhenHeadersReady.IsCompleted);
        Assert.True(feature.HasStarted);
    }

    [Fact(Timeout = 5000)]
    public async Task WhenHeadersReady_should_complete_on_first_BodySink_write()
    {
        var feature = new TurboHttpResponseBodyFeature();
        var chunk = new ReadOnlyMemory<byte>("data"u8.ToArray());

        Assert.False(feature.WhenHeadersReady.IsCompleted);

        await Source.Single(chunk).RunWith(feature.BodySink, Sys.Materializer());

        Assert.True(feature.WhenHeadersReady.IsCompleted);
        Assert.True(feature.HasStarted);

        await feature.CompleteAsync();
    }

    [Fact(Timeout = 5000)]
    public void WhenHeadersReady_should_not_be_completed_initially()
    {
        var feature = new TurboHttpResponseBodyFeature();

        Assert.False(feature.WhenHeadersReady.IsCompleted);
        Assert.False(feature.HasStarted);
    }

    [Fact(Timeout = 5000)]
    public async Task Completed_single_segment_body_should_be_served_without_copy()
    {
        // The buffered-body fast path hands out the pipe's own segment instead of a
        // ToArray copy; the segment stays valid until the feature is reset.
        var ct = TestContext.Current.CancellationToken;
        var feature = new TurboHttpResponseBodyFeature();

        await feature.Writer.WriteAsync("Hello, World!"u8.ToArray(), ct);
        await feature.CompleteAsync();

        Assert.True(feature.TryGetBufferedBody(out var body));
        Assert.Equal("Hello, World!", Encoding.UTF8.GetString(body.Span));
    }

    [Fact(Timeout = 5000)]
    public async Task TryGetBufferedBody_should_not_expose_incomplete_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var feature = new TurboHttpResponseBodyFeature();

        await feature.Writer.WriteAsync("partial"u8.ToArray(), ct);

        Assert.False(feature.TryGetBufferedBody(out _),
            "An incomplete buffered body must not be emitted as a finished response.");
    }
}