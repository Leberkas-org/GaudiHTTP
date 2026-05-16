namespace TurboHTTP.Protocol.Multiplexed.Body;

internal static class BodyDecoderFactory
{
    public static IBodyDecoder Create(bool streaming)
    {
        return streaming
            ? new StreamingBodyDecoder()
            : new BufferedBodyDecoder();
    }
}
