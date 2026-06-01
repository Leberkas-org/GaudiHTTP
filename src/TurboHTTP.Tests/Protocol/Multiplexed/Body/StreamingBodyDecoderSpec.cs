using TurboHTTP.Protocol.Multiplexed.Body;

namespace TurboHTTP.Tests.Protocol.Multiplexed.Body;

public sealed class StreamingBodyDecoderSpec
{
    [Fact(Timeout = 5000)]
    public async Task StreamingBodyDecoder_should_stream_data_through_content()
    {
        using var decoder = new StreamingBodyDecoder(long.MaxValue);
        decoder.Feed("Hello"u8, endStream: false);
        decoder.Feed(" Stream"u8, endStream: true);

        Assert.True(decoder.IsComplete);
        var bodyStream = decoder.GetBodyStream();
        var bytes = await new StreamContent(bodyStream).ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello Stream"u8.ToArray(), bytes);
    }

    [Fact(Timeout = 5000)]
    public void StreamingBodyDecoder_should_abort_cleanly()
    {
        using var decoder = new StreamingBodyDecoder(long.MaxValue);
        decoder.Feed("partial"u8, endStream: false);
        decoder.Abort();
        Assert.False(decoder.IsComplete);
    }
}