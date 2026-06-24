using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.LineBased;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http11.Options;

namespace GaudiHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerEncoder(Http11ServerEncoderOptions options)
{
    private readonly HeaderCollection _reusableHeaders = new();

    public int Encode(Span<byte> destination, IFeatureCollection features, bool isChunked = false, bool connectionClose = false)
    {
        var writer = SpanWriter.Create(destination);

        var responseFeature = features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        StatusLineWriter.Write(ref writer, HttpVersion.Version11, statusCode);

        _reusableHeaders.Clear();
        var responseHeaders = responseFeature?.Headers;
        if (responseHeaders is not null)
        {
            foreach (var h in responseHeaders)
            {
                if (ConnectionSemantics.IsHopByHop(h.Key))
                {
                    continue;
                }

                foreach (var v in h.Value)
                {
                    if (v is not null)
                    {
                        _reusableHeaders.Add(h.Key, v);
                    }
                }
            }
        }

        if (isChunked)
        {
            if (!_reusableHeaders.Contains(WellKnownHeaders.TransferEncoding))
            {
                _reusableHeaders.Add(WellKnownHeaders.TransferEncoding, WellKnownHeaders.ChunkedValue);
            }

            var trailerFeature = features.Get<IHttpResponseTrailersFeature>();
            if (trailerFeature?.Trailers is { Count: > 0 } trailers
                && !_reusableHeaders.Contains(WellKnownHeaders.Trailer))
            {
                var trailerNames = BuildTrailerNames(trailers);
                if (trailerNames.Length > 0)
                {
                    _reusableHeaders.Add(WellKnownHeaders.Trailer, trailerNames);
                }
            }
        }
        else if (!_reusableHeaders.Contains(WellKnownHeaders.ContentLength))
        {
            _reusableHeaders.Add(WellKnownHeaders.ContentLength, ContentLengthCache.GetValue(0L));
        }

        if (options.WriteDateHeader && !_reusableHeaders.Contains(WellKnownHeaders.Date))
        {
            _reusableHeaders.Add(WellKnownHeaders.Date, DateHeaderCache.GetValue());
        }

        if (connectionClose)
        {
            _reusableHeaders.Add(WellKnownHeaders.Connection, WellKnownHeaders.CloseValue);
        }

        HeaderBlockWriter.Write(ref writer, _reusableHeaders);

        // Body encoding is handled separately via the BodySink
        return writer.BytesWritten;
    }

    private static string BuildTrailerNames(IHeaderDictionary trailers)
    {
        var first = true;
        var names = string.Empty;
        foreach (var header in trailers)
        {
            if (!TrailerFieldValidator.IsAllowedInTrailer(header.Key))
            {
                continue;
            }

            names = first
                ? header.Key
                : string.Concat(names, ", ", header.Key);
            first = false;
        }

        return names;
    }
}