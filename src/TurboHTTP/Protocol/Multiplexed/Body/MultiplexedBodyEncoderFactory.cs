namespace TurboHTTP.Protocol.Multiplexed.Body;

internal static class BodyEncoderFactory
{
    public static IBodyEncoder? Create(HttpContent? content)
    {
        if (content is null)
        {
            return null;
        }

        if (content.Headers.ContentLength is not null)
        {
            return new BufferedBodyEncoder();
        }

        return new StreamingBodyEncoder();
    }
}
