using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.RoundTrip;

public sealed class Http11RoundTripFragmentationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_response_when_split_after_status_line()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(full);

        // "HTTP/1.1 200 OK\r\n" = 17 bytes
        const int splitAt = 17;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);
        var outcome1 = decoder.Feed(part1.Span, false, out _);
        var outcome2 = decoder.Feed(part2.Span, false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome1);
        Assert.Equal(DecodeOutcome.Complete, outcome2);
        var response = decoder.GetResponse();
        Assert.Equal("hello", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_response_when_split_at_header_body_boundary()
    {
        var headerBytes = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"u8.ToArray();
        var bodyBytes = "hello"u8.ToArray();

        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);
        var outcome1 = decoder.Feed(headerBytes.AsSpan(), false, out _);
        var outcome2 = decoder.Feed(bodyBytes.AsSpan(), false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome1);
        Assert.Equal(DecodeOutcome.Complete, outcome2);
        var response = decoder.GetResponse();
        Assert.Equal("hello", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_body_when_split_mid_body()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\n0123456789";
        var bytes = Encoding.ASCII.GetBytes(full);
        var headerLen = full.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;

        // Split 5 bytes into the body
        var splitAt = headerLen + 5;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);
        var outcome1 = decoder.Feed(part1.Span, false, out _);
        var outcome2 = decoder.Feed(part2.Span, false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome1);
        Assert.Equal(DecodeOutcome.Complete, outcome2);
        var response = decoder.GetResponse();
        Assert.Equal("0123456789", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_response_when_single_byte_tcp_delivery()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\nabc";
        var bytes = Encoding.ASCII.GetBytes(full);

        // The decoder does not buffer internally between calls, so callers must accumulate
        // unconsumed bytes and re-feed from the start of any incomplete parse unit.
        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);
        var accum = new byte[bytes.Length];
        var accumLen = 0;
        HttpResponseMessage? finalResponse = null;

        for (var i = 0; i < bytes.Length; i++)
        {
            accum[accumLen++] = bytes[i];
            var outcome = decoder.Feed(accum.AsSpan(0, accumLen), false, out var consumed);
            if (consumed > 0)
            {
                accum.AsSpan(consumed, accumLen - consumed).CopyTo(accum);
                accumLen -= consumed;
            }

            if (outcome == DecodeOutcome.Complete)
            {
                finalResponse = decoder.GetResponse();
            }
        }

        Assert.NotNull(finalResponse);
        Assert.Equal("abc", await finalResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTripFragmentation_should_assemble_chunked_body_when_split_between_chunks()
    {
        var part1 =
            (ReadOnlyMemory<byte>)"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nfoo\r\n"u8.ToArray();
        var part2 = (ReadOnlyMemory<byte>)"3\r\nbar\r\n0\r\n\r\n"u8.ToArray();

        var decoder = new Http11ClientDecoder(Http11ClientDecoderOptions.Default);
        var outcome1 = decoder.Feed(part1.Span, false, out _);
        var outcome2 = decoder.Feed(part2.Span, false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome1);
        Assert.Equal(DecodeOutcome.Complete, outcome2);
        var response = decoder.GetResponse();
        Assert.Equal("foobar", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}