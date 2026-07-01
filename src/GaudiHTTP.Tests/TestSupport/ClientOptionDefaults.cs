using GaudiHTTP.Protocol.Syntax.Http10.Options;
using GaudiHTTP.Protocol.Syntax.Http11.Options;
using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Protocol.Syntax.Http3.Options;

namespace GaudiHTTP.Tests.TestSupport;

/// <summary>
/// Test-only factory for the internal per-protocol client decoder/encoder option records.
/// These records used to expose a static <c>Default</c> property; that was removed when the
/// refactor made every member <c>required</c> (production sources the values from
/// <see cref="GaudiHTTP.Client.GaudiClientOptions"/> via the projection layer). The values here
/// reproduce the previous static defaults verbatim so existing specs keep their exact behaviour.
/// </summary>
internal static class ClientOptionDefaults
{
    public static Http10ClientDecoderOptions Http10Decoder() => new()
    {
        StreamingThreshold = 64 * 1024,
        MaxBufferedBodySize = 4 * 1024 * 1024,
        MaxStreamedBodySize = null,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
        HeaderLineMaxLength = 8 * 1024,
        MaxChunkedControlLineLength = 64 * 1024,
        MaxChunkedTrailerSize = 32 * 1024,
        AllowObsFold = false,
    };

    public static Http11ClientDecoderOptions Http11Decoder() => new()
    {
        StreamingThreshold = 64 * 1024,
        MaxBufferedBodySize = 4 * 1024 * 1024,
        MaxStreamedBodySize = null,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
        HeaderLineMaxLength = 8 * 1024,
        MaxChunkExtensionLength = int.MaxValue,
        MaxChunkedControlLineLength = 64 * 1024,
        MaxChunkedTrailerSize = 32 * 1024,
        AllowObsFold = false,
    };

    public static Http11ClientEncoderOptions Http11Encoder() => new()
    {
        AutoHost = true,
        AutoAcceptEncoding = true,
    };

    public static Http2ClientDecoderOptions Http2Decoder() => new()
    {
        MaxConcurrentStreams = 100,
        InitialConnectionWindowSize = 64 * 1024 * 1024,
        InitialStreamWindowSize = 1 * 1024 * 1024,
        MaxStreamWindowSize = 16 * 1024 * 1024,
        WindowScaleThresholdMultiplier = 1.0,
        EnableAdaptiveWindowScaling = true,
        MaxHeaderSize = 16 * 1024,
        MaxHeaderListSize = 64 * 1024,
    };

    public static Http2ClientEncoderOptions Http2Encoder() => new()
    {
        HeaderTableSize = 64 * 1024,
        MaxFrameSize = 16 * 1024,
    };

    public static Http3ClientDecoderOptions Http3Decoder() => new()
    {
        MaxConcurrentStreams = 100,
        MaxFieldSectionSize = 64 * 1024,
    };

    public static Http3ClientEncoderOptions Http3Encoder() => new()
    {
        QpackMaxTableCapacity = 16 * 1024,
        QpackBlockedStreams = 100,
    };
}
