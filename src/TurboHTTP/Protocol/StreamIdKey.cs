namespace TurboHTTP.Protocol;

internal static class StreamIdKey
{
    public static readonly HttpRequestOptionsKey<int> Http2 = new("TurboHTTP.StreamId.H2");
    public static readonly HttpRequestOptionsKey<long> Http3 = new("TurboHTTP.StreamId.H3");
}
