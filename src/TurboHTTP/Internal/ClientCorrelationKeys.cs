namespace TurboHTTP.Internal;

internal static class OptionsKey
{
    internal static readonly HttpRequestOptionsKey<Guid> ConsumerIdKey = new("TurboHTTP.ConsumerId");
    internal static readonly HttpRequestOptionsKey<PendingRequest> Key = new("TurboHTTP.PendingRequest");
    internal static readonly HttpRequestOptionsKey<short> VersionKey = new("TurboHTTP.Version");
    internal static readonly HttpRequestOptionsKey<int> Http2 = new("TurboHTTP.StreamId.H2");
    internal static readonly HttpRequestOptionsKey<long> Http3 = new("TurboHTTP.StreamId.H3");
}