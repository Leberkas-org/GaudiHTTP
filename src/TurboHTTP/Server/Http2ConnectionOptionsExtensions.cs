using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http2.Options;

namespace TurboHTTP.Server;

internal static class Http2ConnectionOptionsExtensions
{
    public static BodyEncoderOptions ToBodyEncoderOptions(this Http2ConnectionOptions o) => new()
    {
        ChunkSize = o.ResponseBodyChunkSize
    };

    public static Http2ServerEncoderOptions ToEncoderOptions(this Http2ConnectionOptions o) => new()
    {
        MaxFrameSize = o.MaxFrameSize,
        HeaderTableSize = o.HeaderTableSize,
        WriteDateHeader = true,
        MaxHeaderBytes = o.MaxHeaderListSize,
        UseHuffman = o.UseHuffman,
    };

    public static Http2ServerDecoderOptions ToDecoderOptions(this Http2ConnectionOptions o) => new()
    {
        HeaderTableSize = o.HeaderTableSize,
        MaxConcurrentStreams = o.MaxConcurrentStreams,
        MaxFieldSectionSize = o.MaxHeaderListSize,
        MaxHeaderBytes = o.MaxHeaderListSize,
        MaxHeaderCount = o.MaxHeaderCount,
        InitialConnectionWindowSize = o.InitialConnectionWindowSize,
        InitialStreamWindowSize = o.InitialStreamWindowSize,
        MaxStreamWindowSize = o.MaxStreamWindowSize,
        WindowScaleThresholdMultiplier = o.WindowScaleThresholdMultiplier,
        EnableAdaptiveWindowScaling = o.EnableAdaptiveWindowScaling,
    };
}