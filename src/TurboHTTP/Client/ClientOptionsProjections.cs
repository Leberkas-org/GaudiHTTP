using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Protocol.Syntax.Http3.Options;

namespace TurboHTTP.Client;

/// <summary>
/// Projects the public <see cref="TurboClientOptions"/> onto the per-protocol decoder/encoder
/// option records, mirroring the server-side ServerOptionsProjections. State machines call these
/// instead of constructing the option records inline.
/// </summary>
internal static class ClientOptionsProjections
{
    public static Http10ClientDecoderOptions ToHttp10DecoderOptions(this TurboClientOptions o) => new()
    {
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxHeaderBytes = o.Http1.MaxResponseHeadersLength * 1024,
    };

    public static Http11ClientDecoderOptions ToHttp11DecoderOptions(this TurboClientOptions o) => new()
    {
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxHeaderBytes = o.Http1.MaxResponseHeadersLength * 1024,
    };

    public static Http11ClientEncoderOptions ToHttp11EncoderOptions(this TurboClientOptions o) => new()
    {
        AutoHost = o.Http1.AutoHost,
        AutoAcceptEncoding = o.Http1.AutoAcceptEncoding,
    };

    public static Http2ClientDecoderOptions ToHttp2DecoderOptions(this TurboClientOptions o) => new()
    {
        MaxConcurrentStreams = o.Http2.MaxConcurrentStreams,
        InitialConnectionWindowSize = o.Http2.InitialConnectionWindowSize,
        InitialStreamWindowSize = o.Http2.InitialStreamWindowSize,
    };

    public static Http2ClientEncoderOptions ToHttp2EncoderOptions(this TurboClientOptions o) => new()
    {
        HeaderTableSize = o.Http2.HeaderTableSize,
    };

    public static Http3ClientDecoderOptions ToHttp3DecoderOptions(this TurboClientOptions o) => new()
    {
        MaxConcurrentStreams = o.Http3.MaxConcurrentStreams,
        MaxFieldSectionSize = o.Http3.MaxFieldSectionSize,
    };

    public static Http3ClientEncoderOptions ToHttp3EncoderOptions(this TurboClientOptions o) => new()
    {
        QpackMaxTableCapacity = o.Http3.QpackMaxTableCapacity,
        QpackBlockedStreams = o.Http3.QpackBlockedStreams,
    };
}
