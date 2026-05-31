using TurboHTTP.Protocol.Syntax.Http3.Options;

namespace TurboHTTP.Server;

internal static class Http3ConnectionOptionsExtensions
{
    public static Http3ServerEncoderOptions ToEncoderOptions(this Http3ConnectionOptions o) => new()
    {
        WriteDateHeader = true,
        QpackMaxTableCapacity = o.QpackMaxTableCapacity,
        QpackBlockedStreams = o.QpackBlockedStreams,
        MaxHeaderBytes = o.MaxHeaderListSize,
    };

    public static Http3ServerDecoderOptions ToDecoderOptions(this Http3ConnectionOptions o) => new()
    {
        MaxConcurrentStreams = o.MaxConcurrentStreams,
        MaxFieldSectionSize = o.MaxHeaderListSize,
        MaxHeaderBytes = o.MaxHeaderListSize,
        MaxHeaderCount = o.MaxHeaderCount,
    };
}
