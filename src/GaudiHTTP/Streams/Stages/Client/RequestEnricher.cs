using System.Net;
using System.Net.Http.Headers;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Streams.Stages.Client;

/// <summary>
/// Stateless request enrichment logic extracted from the former <see cref="RequestEnricher"/>.
/// Applied as a <c>Select()</c> transform in the pipeline — no separate GraphStage needed.
/// Handles: URI resolution, version defaults, header merging, Referer sanitization,
/// If-Range validation, and default timeout injection for the channel path.
/// </summary>
internal sealed class RequestEnricher(Func<GaudiRequestOptions> optionsFactory)
{
    public HttpRequestMessage Enrich(HttpRequestMessage request)
    {
        var options = optionsFactory.Invoke();

        // Rule 1: URI resolution
        if (request.RequestUri is null || !request.RequestUri.IsAbsoluteUri)
        {
            var baseAddress = options.BaseAddress;
            if (baseAddress is null)
            {
                throw new InvalidOperationException(
                    "RequestUri is null or relative but no BaseAddress is configured.");
            }

            request.RequestUri = request.RequestUri is null
                ? baseAddress
                : new Uri(baseAddress, request.RequestUri);
        }

        // Rule 2: Version — only override when request is still at the 1.1 default
        if (request.Version == HttpVersion.Version11 && options.DefaultRequestVersion != HttpVersion.Version11)
        {
            request.Version = options.DefaultRequestVersion;
        }

        // Rule 2b: VersionPolicy — only override when request is still at the default (RequestVersionOrLower)
        if (request.VersionPolicy == HttpVersionPolicy.RequestVersionOrLower
            && options.DefaultVersionPolicy != HttpVersionPolicy.RequestVersionOrLower)
        {
            request.VersionPolicy = options.DefaultVersionPolicy;
        }

        // Rule 2c: HTTP/3 cannot traverse an HTTP forward proxy — QUIC would silently bypass it.
        // Downgrade to HTTP/2 (TLS + CONNECT tunnel) when the policy allows, otherwise fail.
        if (request.Version.Major >= 3 && ProxyApplies(options, request.RequestUri))
        {
            if (request.VersionPolicy == HttpVersionPolicy.RequestVersionOrLower)
            {
                request.Version = HttpVersion.Version20;
            }
            else
            {
                throw new HttpRequestException(
                    "HTTP/3 cannot be used through an HTTP proxy. Use HttpVersionPolicy.RequestVersionOrLower to allow a downgrade, or bypass the proxy for this host.");
            }
        }

        // Rule 3: Default headers — add those absent from the request
        foreach (var header in options.DefaultRequestHeaders)
        {
            if (!request.Headers.Contains(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Rule 5: PreAuthenticate — inject Authorization header when credentials are available
        if (options is { PreAuthenticate: true, Credentials: not null } && !request.Headers.Contains(WellKnownHeaders.Authorization))
        {
            InjectAuthorization(request, options.Credentials);
        }

        // Rule 6: Referer sanitization (RFC 9110 §10.5)
        SanitizeReferer(request);

        // Rule 7: If-Range validation (RFC 9110 §13.1.5)
        IfRangeValidator.Validate(request);

        // Rule 8: Default timeout — inject CancellationToken when none is set.
        // SendAsync sets the token itself; this covers the channel path.
        if (!request.Options.TryGetValue(OptionsKey.CancellationTokenKey, out _))
        {
            var timeout = request.Options.TryGetValue(OptionsKey.TimeoutKey, out var perRequest)
                ? perRequest
                : options.Timeout;

            if (timeout != System.Threading.Timeout.InfiniteTimeSpan
                && timeout > TimeSpan.Zero
                && timeout < TimeSpan.FromDays(1))
            {
                var cts = new CancellationTokenSource(timeout);
                request.SetCancellationToken(cts.Token);

                if (request.Options.TryGetValue(OptionsKey.Key, out var pending))
                {
                    cts.Token.UnsafeRegister(
                        static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                        pending);

                    // Hand ownership of the timer-backed source to the pending request so it is
                    // disposed the moment the response is delivered, instead of lingering (and holding
                    // a TimerQueue slot) for the whole timeout window after every channel-path request.
                    pending.AttachTimeoutCts(cts);
                }
            }
        }

        return request;
    }

    internal static bool ProxyApplies(GaudiRequestOptions options, Uri? requestUri)
    {
        return options is { UseProxy: true, Proxy: not null }
               && requestUri is not null
               && !options.Proxy.IsBypassed(requestUri);
    }

    /// <summary>
    /// Injects a Basic Authorization header using the supplied credentials.
    /// Uses <see cref="ICredentials.GetCredential"/> with the request URI and "Basic" scheme.
    /// </summary>
    private static void InjectAuthorization(HttpRequestMessage request, ICredentials credentials)
    {
        if (request.RequestUri is null)
        {
            return;
        }

        var credential = credentials.GetCredential(request.RequestUri, "Basic");
        if (credential is null)
        {
            return;
        }

        var encoded = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{credential.UserName}:{credential.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }

    /// <summary>
    /// RFC 9110 §10.5:
    /// - Strip fragment and userinfo from Referer URI
    /// - Remove Referer on HTTPS→HTTP downgrade
    /// </summary>
    private static void SanitizeReferer(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues(WellKnownHeaders.Referer, out var values))
        {
            return;
        }

        string? refererValue = null;
        foreach (var v in values)
        {
            refererValue = v;
            break;
        }

        if (string.IsNullOrEmpty(refererValue) || !Uri.TryCreate(refererValue, UriKind.Absolute, out var refererUri))
        {
            return;
        }

        // HTTPS→HTTP downgrade: remove Referer entirely
        if (refererUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            && request.RequestUri is not null
            && request.RequestUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Remove(WellKnownHeaders.Referer);
            return;
        }

        // Strip fragment and userinfo
        var needsStrip = !string.IsNullOrEmpty(refererUri.Fragment)
                         || !string.IsNullOrEmpty(refererUri.UserInfo);

        if (!needsStrip) return;
        var sanitized = UriSanitizer.FormatAbsoluteWithoutUserInfo(refererUri);
        request.Headers.Remove(WellKnownHeaders.Referer);
        request.Headers.TryAddWithoutValidation(WellKnownHeaders.Referer, sanitized);
    }
}
