using System.Threading;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Streams.IO;
using Servus.Akka.Sse;
using TurboHTTP.Internal;

namespace TurboHTTP.Client;

/// <summary>
/// Extension methods for <see cref="HttpRequestMessage"/> and <see cref="HttpResponseMessage"/>
/// that integrate with the TurboHTTP pipeline.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Sets a per-request timeout that overrides the client's global <see cref="ITurboHttpClient.Timeout"/>
    /// for this request only. If no response arrives within <paramref name="timeout"/>, the request is
    /// cancelled and <c>SendAsync</c> throws an <see cref="OperationCanceledException"/>.
    /// </summary>
    public static HttpRequestMessage WithTimeout(this HttpRequestMessage request, TimeSpan timeout)
    {
        request.Options.Set(OptionsKey.TimeoutKey, timeout);
        return request;
    }

    /// <summary>
    /// Declares the first-party context (the site initiating this request) so the cookie jar can
    /// enforce the <c>SameSite</c> attribute (RFC 6265bis §5.8.3). When set and the request target is
    /// cross-site relative to <paramref name="firstParty"/>, <c>SameSite=Strict</c> cookies are withheld,
    /// and <c>SameSite=Lax</c> cookies are withheld on unsafe methods. When unset, requests are treated
    /// as first-party.
    /// </summary>
    public static HttpRequestMessage WithFirstPartyContext(this HttpRequestMessage request, Uri firstParty)
    {
        request.Options.Set(OptionsKey.FirstPartyContextKey, firstParty);
        return request;
    }

    /// <summary>
    /// Attaches a <see cref="PendingRequest"/> correlation ticket to <paramref name="request"/> and
    /// returns a <see cref="ValueTask{TResult}"/> that completes when the pipeline delivers the matching response.
    /// Intended for use with the channel-based <see cref="ITurboHttpClient.Requests"/> API.
    /// </summary>
    public static ValueTask<HttpResponseMessage> GetResponseAsync(this HttpRequestMessage request,
        CancellationToken ct = default)
    {
        var pending = PendingRequest.Rent();
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, pending.Version);

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                pending);
        }

        return pending.GetValueTask();
    }

    /// <summary>
    /// Converts an HttpResponseMessage content stream into a reactive Source of ServerSentEvent.
    /// Uses the SSE parser GraphStage to parse binary content into structured events.
    /// </summary>
    /// <param name="response">The HTTP response message containing SSE data</param>
    /// <returns>Source that emits ServerSentEvent records from the response body</returns>
    /// <remarks>
    /// The returned Source reads from the response content stream and parses SSE
    /// according to RFC 9110. The response must have a stream-compatible content.
    /// </remarks>
    public static Source<ServerSentEvent, NotUsed> AsEventStream(this HttpResponseMessage response)
    {
        return StreamSource.From(response.Content.ReadAsStream())
            .Via(SseParserFlow.Instance);
    }

    internal static void SetCancellationToken(this HttpRequestMessage request, CancellationToken ct)
    {
        request.Options.Set(OptionsKey.CancellationTokenKey, ct);
    }

    internal static CancellationToken GetCancellationToken(this HttpRequestMessage request)
    {
        return request.Options.TryGetValue(OptionsKey.CancellationTokenKey, out var ct)
            ? ct
            : CancellationToken.None;
    }
}