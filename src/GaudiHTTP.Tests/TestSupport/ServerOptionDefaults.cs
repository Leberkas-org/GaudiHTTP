using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.TestSupport;

/// <summary>
/// Shared factory for the internal per-protocol server connection option records
/// (<see cref="Http2ConnectionOptions"/>, <see cref="Http3ConnectionOptions"/>).
/// Session-manager specs previously each declared their own private
/// <c>DefaultOptions()</c> with byte-for-byte identical values — this centralizes
/// them so the values are defined once.
/// </summary>
internal static class ServerOptionDefaults
{
    public static ResolvedServerLimits Limits() => new(
        MaxRequestBodySize: 30 * 1024 * 1024,
        KeepAliveTimeout: TimeSpan.FromSeconds(130),
        RequestHeadersTimeout: TimeSpan.FromSeconds(30),
        MinRequestBodyDataRate: 240,
        MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(5),
        MinResponseDataRate: 240,
        MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(5),
        MaxResetStreamsPerWindow: 200,
        RapidResetDetectionWindow: TimeSpan.FromSeconds(30));

    public static byte[] BuildHttp3Request(string method, string path)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", method),
            (":path", path),
            (":scheme", "https"),
            (":authority", "localhost"),
        };
        var headerBlock = tableSync.Encoder.Encode(headers);
        var frame = new HeadersFrame(headerBlock);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    public static Http2ConnectionOptions Http2() => new()
    {
        Limits = Limits(),
        MaxConcurrentStreams = 100,
        InitialConnectionWindowSize = 64 * 1024 * 1024,
        InitialStreamWindowSize = 1 * 1024 * 1024,
        MaxStreamWindowSize = 16 * 1024 * 1024,
        WindowScaleThresholdMultiplier = 1.0,
        EnableAdaptiveWindowScaling = true,
        MaxFrameSize = 16 * 1024,
        HeaderTableSize = 64 * 1024,
        MaxHeaderListSize = 32 * 1024,
        MaxHeaderCount = 100,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
        MaxBufferedBodySize = 64 * 1024,
        ResponseBodyChunkSize = 16 * 1024,
    };

    public static Http3ConnectionOptions Http3() => new()
    {
        Limits = Limits(),
        MaxConcurrentStreams = 100,
        MaxHeaderListSize = 32 * 1024,
        MaxHeaderCount = 100,
        QpackMaxTableCapacity = 0,
        QpackBlockedStreams = 0,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
        MaxBufferedBodySize = 64 * 1024,
        ResponseBodyChunkSize = 16 * 1024,
    };
}
