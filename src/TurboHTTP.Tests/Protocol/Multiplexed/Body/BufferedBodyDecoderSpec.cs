using TurboHTTP.Protocol.Multiplexed.Body;

namespace TurboHTTP.Tests.Protocol.Multiplexed.Body;

public sealed class BufferedBodyDecoderSpec
{
    [Fact(Timeout = 5000)]
    public async Task BufferedBodyDecoder_should_accumulate_data_and_produce_content()
    {
        using var decoder = new BufferedBodyDecoder();
        decoder.Feed("Hello, "u8, endStream: false);
        decoder.Feed("World!"u8, endStream: true);

        Assert.True(decoder.IsComplete);
        var bodyStream = decoder.GetBodyStream();
        var bytes = await new StreamContent(bodyStream).ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello, World!"u8.ToArray(), bytes);
    }

    [Fact(Timeout = 5000)]
    public async Task BufferedBodyDecoder_should_handle_empty_body()
    {
        using var decoder = new BufferedBodyDecoder();
        decoder.Feed(ReadOnlySpan<byte>.Empty, endStream: true);
        Assert.True(decoder.IsComplete);
        var bodyStream = decoder.GetBodyStream();
        var bytes = await new StreamContent(bodyStream).ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(bytes);
    }

    [Fact(Timeout = 5000)]
    public async Task BufferedBodyDecoder_should_handle_single_large_chunk()
    {
        using var decoder = new BufferedBodyDecoder();
        var data = new byte[32_768];
        Random.Shared.NextBytes(data);
        decoder.Feed(data, endStream: true);
        Assert.True(decoder.IsComplete);
        var bodyStream = decoder.GetBodyStream();
        var content = new StreamContent(bodyStream);
        var result = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(data, result);
    }
}