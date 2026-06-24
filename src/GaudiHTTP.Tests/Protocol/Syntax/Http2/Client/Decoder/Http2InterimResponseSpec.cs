using System.Net;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder;

public sealed class Http2InterimResponseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeaders_should_not_set_HasResponse_for_100_continue()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "100")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);

        decoder.DecodeHeaders(streamId: 1, endStream: false, state);

        Assert.False(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeaders_should_not_set_HasResponse_for_103_early_hints()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "103")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);

        decoder.DecodeHeaders(streamId: 1, endStream: false, state);

        Assert.False(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeaders_should_return_interim_response_object()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "100")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);

        var response = decoder.DecodeHeaders(streamId: 1, endStream: false, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.Continue, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeaders_should_set_HasResponse_for_200_ok()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "200")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);

        decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.True(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeaders_should_allow_final_response_after_100_continue()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var state = new StreamState();
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);

        var interimEncoded = encoder.Encode([(":status", "100")]);
        state.AppendHeader(interimEncoded.Span);
        decoder.DecodeHeaders(streamId: 1, endStream: false, state);

        state.ClearHeaderBuffer();

        var finalEncoded = encoder.Encode([(":status", "200"), ("content-type", "text/plain")]);
        state.AppendHeader(finalEncoded.Span);
        var finalResponse = decoder.DecodeHeaders(streamId: 1, endStream: true, state);

        Assert.NotNull(finalResponse);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        Assert.True(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeHeadersForStreaming_should_not_set_HasResponse_for_1xx()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var encoded = encoder.Encode([(":status", "103")]);
        var state = new StreamState();
        state.AppendHeader(encoded.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);

        var response = decoder.DecodeHeadersForStreaming(streamId: 1, state);

        Assert.NotNull(response);
        Assert.Equal((HttpStatusCode)103, response.StatusCode);
        Assert.False(state.HasResponse);
    }
}
