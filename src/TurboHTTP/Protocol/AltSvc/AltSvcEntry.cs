namespace TurboHTTP.Protocol.AltSvc;

/// <summary>
/// Represents a single Alt-Svc directive parsed from an Alt-Svc header (RFC 7838 §3).
/// </summary>
/// <param name="Protocol">The ALPN protocol identifier (e.g., "h3", "h2").</param>
/// <param name="Host">The alternative authority host. Empty string means same host as the origin.</param>
/// <param name="Port">The alternative authority port.</param>
/// <param name="MaxAge">Duration in seconds that the entry is valid. Default is 86400 (24 hours) per RFC 7838 §3.1.</param>
/// <param name="Persist">When true, the entry survives network changes (e.g., moving to a new network). RFC 7838 §3.1.</param>
/// <param name="ExpiresAt">Absolute UTC time when this entry expires, computed as creation time + MaxAge.</param>
public sealed record AltSvcEntry(
    string Protocol,
    string Host,
    int Port,
    int MaxAge,
    bool Persist,
    DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// Returns true if this entry advertises HTTP/3 (ALPN "h3").
    /// </summary>
    public bool IsHttp3 => Protocol.Equals("h3", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if this entry has not yet expired.
    /// </summary>
    public bool IsValid(DateTimeOffset now) => now < ExpiresAt;
}
