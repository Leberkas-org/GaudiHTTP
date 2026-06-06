using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.LineBased.Body;

/// <summary>
/// Builds the body-codec <see cref="BodyDecoderOptions"/> from the per-protocol line-based
/// decoder options, so client and server decoders project rather than construct inline.
/// </summary>
internal static class BodyDecoderOptionsExtensions
{
    public static BodyDecoderOptions ToBodyDecoderOptions(this Http10ClientDecoderOptions o) => new()
    {
        StreamingThreshold = o.StreamingThreshold,
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxChunkExtensionLength = int.MaxValue,
    };

    public static BodyDecoderOptions ToBodyDecoderOptions(this Http11ClientDecoderOptions o) => new()
    {
        StreamingThreshold = o.StreamingThreshold,
        MaxBufferedBodySize = o.MaxBufferedBodySize,
        MaxStreamedBodySize = o.MaxStreamedBodySize,
        MaxChunkExtensionLength = o.MaxChunkExtensionLength,
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