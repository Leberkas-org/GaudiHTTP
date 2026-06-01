using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Client;

/// <summary>
/// Configuration for the automatic redirect-following policy applied to HTTP responses
/// with 3xx status codes. Pass to <c>WithRedirect</c> on an <see cref="ITurboHttpClientBuilder"/>.
/// </summary>
public sealed class RedirectOptions
{
    /// <summary>
    /// Maximum number of redirects to follow before throwing <see cref="RedirectException"/>.
    /// Default is 10.
    /// </summary>
    public int MaxRedirects { get; set; } = 10;

    /// <summary>
    /// If true, allows redirects from HTTPS to HTTP.
    /// Default is false (downgrade blocked by default for security).
    /// </summary>
    public bool AllowHttpsToHttpDowngrade { get; set; }

    internal RedirectPolicy To() => new()
    {
        MaxRedirects = MaxRedirects,
        AllowHttpsToHttpDowngrade = AllowHttpsToHttpDowngrade
    };
}