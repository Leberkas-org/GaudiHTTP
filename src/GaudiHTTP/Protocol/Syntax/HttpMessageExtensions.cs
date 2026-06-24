using System.Net.Http.Headers;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.Syntax;

internal static class HttpMessageExtensions
{
    public static string ResolveTarget(this HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            return "/";
        }

        return request.RequestUri.IsAbsoluteUri ? request.RequestUri.PathAndQuery : request.RequestUri.OriginalString;
    }

    public static HeaderCollection GetHeaderCollection(this HttpRequestMessage request)
    {
        var headerCollection = new HeaderCollection();
        request.Headers.GetHeaderCollection(ref headerCollection);
        request.Content?.GetHeaderCollection(ref headerCollection);
        return headerCollection;
    }

    public static HeaderCollection GetHeaderCollection(this HttpResponseMessage response)
    {
        var headerCollection = new HeaderCollection();
        response.Headers.GetHeaderCollection(ref headerCollection);
        return headerCollection;
    }

    private static void GetHeaderCollection(this HttpHeaders headers, ref HeaderCollection collection)
    {
        foreach (var h in headers)
        {
            if (ConnectionSemantics.IsHopByHop(h.Key))
            {
                continue;
            }

            if (string.Equals(h.Key, WellKnownHeaders.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var v in h.Value)
            {
                var value = string.Equals(h.Key, "Referer", StringComparison.OrdinalIgnoreCase)
                    ? StripFragment(v)
                    : v;
                collection.Add(h.Key, value);
            }
        }
    }

    private static string StripFragment(string uri)
    {
        var idx = uri.IndexOf('#');
        return idx >= 0 ? uri[..idx] : uri;
    }

    private static void GetHeaderCollection(this HttpContent content, ref HeaderCollection collection)
    {
        foreach (var h in content.Headers)
        {
            if (string.Equals(h.Key, WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var v in h.Value)
            {
                collection.Add(h.Key, v);
            }
        }
    }
}
