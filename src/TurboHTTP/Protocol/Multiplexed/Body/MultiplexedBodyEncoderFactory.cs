namespace TurboHTTP.Protocol.Multiplexed.Body;

internal static class BodyEncoderFactory
{
    public static IBodyEncoder? Create(Stream? bodyStream, long? contentLength)
    {
        if (bodyStream is null)
        {
            return null;
        }

        if (contentLength is not null)
        {
            return new BufferedBodyEncoder();
        }

        return new StreamingBodyEncoder();
    }
}
