using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Protocol.Syntax.Http3.Options;

namespace GaudiHTTP.Tests.TestSupport;

internal static class DecoderEncoderDefaults
{
    public static Http2ServerDecoderOptions Http2Decoder() => new()
    {
        HeaderTableSize = 16 * 1024,
        MaxConcurrentStreams = 100,
        MaxFieldSectionSize = 64 * 1024,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
    };

    public static Http3ServerDecoderOptions Http3Decoder() => new()
    {
        MaxConcurrentStreams = 100,
        MaxFieldSectionSize = 64 * 1024,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
    };

    public static Http2ServerEncoderOptions Http2Encoder() => new()
    {
        MaxFrameSize = 16 * 1024,
        HeaderTableSize = 4096,
        WriteDateHeader = false,
        MaxHeaderBytes = 32 * 1024,
        UseHuffman = true,
    };
}
