namespace TurboHTTP.Protocol.Multiplexed.Body;

internal static class BodyDecoderFactory
{
    public static IBodyDecoder Create(bool streaming, long maxBodySize)
    {
        return streaming
            ? new StreamingBodyDecoder(maxBodySize)
            : new BufferedBodyDecoder();
    }
}
