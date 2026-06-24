using GaudiHTTP.Protocol.Syntax.Http10.Options;
using GaudiHTTP.Protocol.Syntax.Http11.Options;

namespace GaudiHTTP.Protocol.Body;

internal static class BodyDecoderOptionsExtensions
{
    public static BodyDecoderOptions ToBodyDecoderOptions(this Http10ClientDecoderOptions o) => new()
    {
        StreamingThreshold = o.StreamingThreshold,
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxChunkExtensionLength = int.MaxValue
    };

    public static BodyDecoderOptions ToBodyDecoderOptions(this Http11ClientDecoderOptions o) => new()
    {
        StreamingThreshold = o.StreamingThreshold,
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxChunkExtensionLength = o.MaxChunkExtensionLength
    };

    public static BodyDecoderOptions ToBodyDecoderOptions(this Http10ServerDecoderOptions o) => new()
    {
        StreamingThreshold = o.StreamingThreshold,
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxChunkExtensionLength = int.MaxValue
    };

    public static BodyDecoderOptions ToBodyDecoderOptions(this Http11ServerDecoderOptions o) => new()
    {
        StreamingThreshold = o.StreamingThreshold,
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxChunkExtensionLength = o.MaxChunkExtensionLength
    };
}