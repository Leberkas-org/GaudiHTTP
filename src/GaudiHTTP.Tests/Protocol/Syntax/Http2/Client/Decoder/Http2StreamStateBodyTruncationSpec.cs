using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder;

/// <summary>
/// RFC 9113 §8.1.1: a stream that ends before (or after) the declared Content-Length is
/// malformed. When <see cref="StreamState.ExpectedBodyLength"/> is set, END_STREAM with a
/// mismatched byte count must fault the body reader so the consumer observes an error
/// instead of a silently truncated body.
/// </summary>
[Trait("RFC", "RFC9113-8.1.1")]
public sealed class Http2StreamStateBodyTruncationSpec
{
    private static (StreamState State, Stream Body) CreateStreamingState(long? expectedLength)
    {
        var state = new StreamState();
        if (expectedLength is { } cl)
        {
            state.AddContentHeader("Content-Length", cl.ToString());
        }

        var reader = new QueuedBodyReader(capacity: 8);
        reader.Reset();
        state.InitBodyReader(reader);
        return (state, state.GetBodyStream());
    }

    private static async Task<byte[]> ReadToEndAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    [Fact(Timeout = 5000)]
    public async Task EndStream_before_content_length_should_fault_body_reader()
    {
        var (state, body) = CreateStreamingState(expectedLength: 10);

        state.FeedBody("12345"u8, endStream: true);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => ReadToEndAsync(body, TestContext.Current.CancellationToken));
        Assert.Contains("Content-Length", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task EndStream_beyond_content_length_should_fault_body_reader()
    {
        var (state, body) = CreateStreamingState(expectedLength: 3);

        state.FeedBody("12345"u8, endStream: true);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => ReadToEndAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task EndStream_at_exact_content_length_should_complete_body()
    {
        var (state, body) = CreateStreamingState(expectedLength: 5);

        state.FeedBody("12345"u8, endStream: true);

        var bytes = await ReadToEndAsync(body, TestContext.Current.CancellationToken);
        Assert.Equal("12345"u8.ToArray(), bytes);
    }

    [Fact(Timeout = 5000)]
    public async Task EndStream_without_expected_length_should_complete_body()
    {
        var (state, body) = CreateStreamingState(expectedLength: null);

        state.FeedBody("12345"u8, endStream: true);

        var bytes = await ReadToEndAsync(body, TestContext.Current.CancellationToken);
        Assert.Equal("12345"u8.ToArray(), bytes);
    }

    [Fact(Timeout = 5000)]
    public async Task Truncation_across_multiple_data_frames_should_fault_body_reader()
    {
        var (state, body) = CreateStreamingState(expectedLength: 12);

        state.FeedBody("1234"u8, endStream: false);
        state.FeedBody("5678"u8, endStream: true);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => ReadToEndAsync(body, TestContext.Current.CancellationToken));
    }
}
