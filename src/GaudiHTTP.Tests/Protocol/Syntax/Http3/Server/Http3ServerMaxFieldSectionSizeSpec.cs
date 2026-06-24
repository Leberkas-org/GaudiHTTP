using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class Http3ServerMaxFieldSectionSizeSpec
{
    private static Http3ServerDecoderOptions DefaultDecoderOptions() => new()
    {
        MaxConcurrentStreams = 100,
        MaxFieldSectionSize = 64 * 1024,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
    };

    private readonly QpackTableSync _encoderTableSync = new(encoderMaxCapacity: 0, decoderMaxCapacity: 0, maxBlockedStreams: 100, configuredEncoderLimit: null);
    private readonly QpackTableSync _decoderTableSync = new(encoderMaxCapacity: 0, decoderMaxCapacity: 0, maxBlockedStreams: 100, configuredEncoderLimit: null);

    private HeadersFrame EncodeAndSync(List<(string Name, string Value)> headers)
    {
        var headerBlock = _encoderTableSync.Encoder.Encode(headers);
        var instructions = _encoderTableSync.Encoder.EncoderInstructions;
        if (!instructions.IsEmpty)
        {
            _decoderTableSync.ProcessEncoderInstructions(instructions.Span);
        }

        return new HeadersFrame(headerBlock);
    }

    private static StreamState MakeState(long streamId = 1)
    {
        var state = new StreamState();
        state.Initialize(streamId);
        return state;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void DecodeHeaders_with_limit_should_reject_headers_exceeding_max_field_section_size()
    {
        var maxFieldSectionSize = 256;
        var decoderOptions = DefaultDecoderOptions() with { MaxFieldSectionSize = maxFieldSectionSize };
        var decoder = new Http3ServerDecoder(_decoderTableSync, decoderOptions);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-large-header", new string('x', 300)),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() =>
            decoder.DecodeHeadersToFeature(frame, state, endStream: true));
        Assert.Contains("SETTINGS_MAX_FIELD_SECTION_SIZE", ex.Message);
        Assert.Contains("RFC 9114", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void DecodeHeaders_with_limit_should_accept_headers_under_max_field_section_size()
    {
        var maxFieldSectionSize = 512;
        var decoderOptions = DefaultDecoderOptions() with { MaxFieldSectionSize = maxFieldSectionSize };
        var decoder = new Http3ServerDecoder(_decoderTableSync, decoderOptions);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-small-header", "value"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var feature = decoder.DecodeHeadersToFeature(frame, state, endStream: true);
        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void DecodeHeaders_many_small_headers_exceeding_max_field_section_size_should_be_rejected()
    {
        var maxFieldSectionSize = 320;
        var decoderOptions = DefaultDecoderOptions() with { MaxFieldSectionSize = maxFieldSectionSize };
        var decoder = new Http3ServerDecoder(_decoderTableSync, decoderOptions);

        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            ("x-header-1", new string('a', 40)),
            ("x-header-2", new string('b', 40)),
            ("x-header-3", new string('c', 40)),
            ("x-header-4", new string('d', 40)),
            ("x-header-5", new string('e', 40)),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var ex = Assert.Throws<HttpProtocolException>(() =>
            decoder.DecodeHeadersToFeature(frame, state, endStream: true));
        Assert.Contains("SETTINGS_MAX_FIELD_SECTION_SIZE", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2.2")]
    public void DecodeHeaders_default_options_should_allow_normal_requests()
    {
        var decoder = new Http3ServerDecoder(_decoderTableSync, DefaultDecoderOptions());

        var headers = new List<(string Name, string Value)>
        {
            (":method", "POST"),
            (":path", "/api/data"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("content-length", "1024"),
            ("user-agent", "test-client/1.0"),
            ("accept-encoding", "gzip, deflate"),
        };

        var frame = EncodeAndSync(headers);
        var state = MakeState();

        var feature = decoder.DecodeHeadersToFeature(frame, state, endStream: true);
        Assert.NotNull(feature);
        Assert.Equal("POST", feature.Method);
        Assert.Equal("/api/data", feature.Path);
    }
}
