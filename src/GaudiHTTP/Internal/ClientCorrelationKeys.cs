namespace GaudiHTTP.Internal;

internal static class OptionsKey
{
    internal static readonly HttpRequestOptionsKey<Guid> ConsumerIdKey = new("GaudiHTTP.ConsumerId");
    internal static readonly HttpRequestOptionsKey<PendingRequest> Key = new("GaudiHTTP.PendingRequest");
    internal static readonly HttpRequestOptionsKey<short> VersionKey = new("GaudiHTTP.Version");
    internal static readonly HttpRequestOptionsKey<TimeSpan> TimeoutKey = new("GaudiHTTP.RequestTimeout");
    internal static readonly HttpRequestOptionsKey<Uri> FirstPartyContextKey = new("GaudiHTTP.FirstPartyContext");
    internal static readonly HttpRequestOptionsKey<CancellationToken> CancellationTokenKey = new("GaudiHTTP.CancellationToken");
}