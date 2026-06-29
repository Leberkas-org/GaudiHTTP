using GaudiHTTP.Protocol.Syntax.Http10.Options;
using GaudiHTTP.Protocol.Syntax.Http11.Options;
using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Protocol.Syntax.Http3.Options;

namespace GaudiHTTP.Client;

/// <summary>
/// Projects the public <see cref="GaudiClientOptions"/> onto the per-protocol decoder/encoder
/// option records, mirroring the server-side ServerOptionsProjections. State machines call these
/// instead of constructing the option records inline.
/// </summary>
internal static class ClientOptionsProjections
{
    public static Http10ClientDecoderOptions ToHttp10DecoderOptions(this GaudiClientOptions o) => new()
    {
        StreamingThreshold = o.Http1.MaxBufferedResponseBodySize,
        MaxBufferedBodySize = o.Http1.MaxBufferedResponseBodySize,
        MaxStreamedBodySize = o.MaxStreamedResponseBodySize,
        MaxHeaderBytes = o.Http1.MaxResponseHeadersLength * 1024,
        MaxHeaderCount = o.Http1.MaxResponseHeaderCount,
        HeaderLineMaxLength = o.Http1.MaxResponseHeaderLineLength,
        AllowObsFold = false
    };

    public static Http11ClientDecoderOptions ToHttp11DecoderOptions(this GaudiClientOptions o) => new()
    {
        StreamingThreshold = o.Http1.MaxBufferedResponseBodySize,
        MaxBufferedBodySize = o.Http1.MaxBufferedResponseBodySize,
        MaxStreamedBodySize = o.MaxStreamedResponseBodySize,
        MaxHeaderBytes = o.Http1.MaxResponseHeadersLength * 1024,
        MaxHeaderCount = o.Http1.MaxResponseHeaderCount,
        HeaderLineMaxLength = o.Http1.MaxResponseHeaderLineLength,
        MaxChunkExtensionLength = o.Http1.MaxChunkExtensionLength,
        AllowObsFold = false
    };

    public static Http11ClientEncoderOptions ToHttp11EncoderOptions(this GaudiClientOptions o) => new()
    {
        AutoHost = o.Http1.AutoHost,
        AutoAcceptEncoding = o.Http1.AutoAcceptEncoding
    };

    public static Http2ClientDecoderOptions ToHttp2DecoderOptions(this GaudiClientOptions o) => new()
    {
        MaxConcurrentStreams = o.Http2.MaxConcurrentStreams,
        InitialConnectionWindowSize = o.Http2.InitialConnectionWindowSize,
        InitialStreamWindowSize = o.Http2.InitialStreamWindowSize,
        MaxStreamWindowSize = o.Http2.MaxStreamWindowSize,
        WindowScaleThresholdMultiplier = o.Http2.WindowScaleThresholdMultiplier,
        EnableAdaptiveWindowScaling = o.Http2.EnableAdaptiveWindowScaling,
        // Single-field limit (RFC 9113 §10.5.1) tracks the configured header-list budget so that
        // raising MaxResponseHeaderListSize also allows a correspondingly larger single field.
        MaxHeaderSize = o.Http2.MaxResponseHeaderListSize,
        MaxHeaderListSize = o.Http2.MaxResponseHeaderListSize
    };

    public static Http2ClientEncoderOptions ToHttp2EncoderOptions(this GaudiClientOptions o) => new()
    {
        HeaderTableSize = o.Http2.HeaderTableSize,
        MaxFrameSize = o.Http2.MaxFrameSize
    };

    public static Http3ClientDecoderOptions ToHttp3DecoderOptions(this GaudiClientOptions o) => new()
    {
        MaxConcurrentStreams = o.Http3.MaxConcurrentStreams,
        MaxFieldSectionSize = o.Http3.MaxFieldSectionSize
    };

    public static Http3ClientEncoderOptions ToHttp3EncoderOptions(this GaudiClientOptions o) => new()
    {
        QpackMaxTableCapacity = o.Http3.QpackMaxTableCapacity,
        QpackBlockedStreams = o.Http3.QpackBlockedStreams
    };
}
