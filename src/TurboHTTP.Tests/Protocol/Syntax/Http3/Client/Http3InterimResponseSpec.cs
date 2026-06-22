using System.Net;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

/// <summary>
/// H3 client must handle 1xx informational responses without setting HasResponse on the
/// stream state. Previously, AssembleHeaders called state.InitResponse() unconditionally,
/// causing the final response's HEADERS to be treated as trailers.
/// </summary>
public sealed class Http3InterimResponseSpec
{
    private static Http3ClientDecoder CreateDecoder()
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        return new Http3ClientDecoder(tableSync, int.MaxValue);
    }

    private static HeadersFrame EncodeHeaders(params (string Name, string Value)[] headers)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var headerBlock = tableSync.Encoder.Encode(headers.ToList());
        return new HeadersFrame(headerBlock);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_not_set_HasResponse_for_100_continue()
    {
        var decoder = CreateDecoder();
        var state = new StreamState();
        state.Initialize(0);

        decoder.DecodeHeaders(EncodeHeaders((":status", "100")), state);

        Assert.False(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_not_set_HasResponse_for_103_early_hints()
    {
        var decoder = CreateDecoder();
        var state = new StreamState();
        state.Initialize(0);

        decoder.DecodeHeaders(EncodeHeaders((":status", "103")), state);

        Assert.False(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_set_HasResponse_for_200_ok()
    {
        var decoder = CreateDecoder();
        var state = new StreamState();
        state.Initialize(0);

        var isNewResponse = decoder.DecodeHeaders(EncodeHeaders((":status", "200")), state);

        Assert.True(isNewResponse);
        Assert.True(state.HasResponse);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void DecodeHeaders_should_allow_final_response_after_1xx()
    {
        var decoder = CreateDecoder();
        var state = new StreamState();
        state.Initialize(0);

        decoder.DecodeHeaders(EncodeHeaders((":status", "100")), state);
        Assert.False(state.HasResponse);

        var isNewResponse = decoder.DecodeHeaders(
            EncodeHeaders((":status", "200"), ("content-type", "text/plain")), state);

        Assert.True(isNewResponse);
        Assert.True(state.HasResponse);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleHeaders_should_not_set_HasResponse_for_1xx()
    {
        var decoder = CreateDecoder();
        var state = new StreamState();
        state.Initialize(0);

        var headers = new List<(string Name, string Value)> { (":status", "103") };
        var result = decoder.AssembleHeaders(headers, state);

        Assert.False(result);
        Assert.False(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void AssembleHeaders_should_allow_final_after_multiple_1xx()
    {
        var decoder = CreateDecoder();
        var state = new StreamState();
        state.Initialize(0);

        decoder.AssembleHeaders([(":status", "100")], state);
        decoder.AssembleHeaders([(":status", "103"), ("link", "</style.css>; rel=preload")], state);

        Assert.False(state.HasResponse);

        var isNew = decoder.AssembleHeaders([(":status", "200"), ("content-length", "5")], state);

        Assert.True(isNew);
        Assert.True(state.HasResponse);
        Assert.Equal(HttpStatusCode.OK, state.GetResponse().StatusCode);
    }
}
