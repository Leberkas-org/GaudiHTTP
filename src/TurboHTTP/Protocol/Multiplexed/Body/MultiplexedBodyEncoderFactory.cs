namespace TurboHTTP.Protocol.Multiplexed.Body;

internal static class BodyEncoderFactory
{
    public static IBodyEncoder? Create(Stream? bodyStream, long? contentLength, BodyEncoderOptions? options = null)
    {
        if (bodyStream is null)
        {
            return null;
        }

        options ??= BodyEncoderOptions.Default;

        if (contentLength is not null)
        {
            return new BufferedBodyEncoder();
        }

        return new StreamingBodyEncoder(options.ChunkSize);
    }
}
