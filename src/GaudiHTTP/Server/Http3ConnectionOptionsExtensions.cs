using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http3.Options;

namespace TurboHTTP.Server;

internal static class Http3ConnectionOptionsExtensions
{
    public static BodyEncoderOptions ToBodyEncoderOptions(this Http3ConnectionOptions o) => new()
    {
        ChunkSize = o.ResponseBodyChunkSize
    };

    public static Http3ServerEncoderOptions ToEncoderOptions(this Http3ConnectionOptions o) => new()
    {
        WriteDateHeader = true,
        QpackMaxTableCapacity = o.QpackMaxTableCapacity,
        QpackBlockedStreams = o.QpackBlockedStreams,
        MaxHeaderBytes = o.MaxHeaderListSize,
        UseHuffman = o.UseHuffman
    };

    public static Http3ServerDecoderOptions ToDecoderOptions(this Http3ConnectionOptions o) => new()
    {
        MaxConcurrentStreams = o.MaxConcurrentStreams,
        MaxFieldSectionSize = o.MaxHeaderListSize,
        MaxHeaderBytes = o.MaxHeaderListSize,
        MaxHeaderCount = o.MaxHeaderCount
    };
}