using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerEncoder(Http10ServerEncoderOptions options)
{
    private readonly HeaderCollection _reusableHeaders = new();

    public int Encode(Span<byte> _, IFeatureCollection features, IActorRef stageActor)
    {
        // HTTP/1.0 always defers — body sink will be handled by caller
        return 0;
    }

    public int EncodeDeferred(Span<byte> destination, IFeatureCollection features, ReadOnlySpan<byte> body)
    {
        var writer = SpanWriter.Create(destination);
        var responseFeature = features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        StatusLineWriter.Write(ref writer, HttpVersion.Version10, statusCode);

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

        _reusableHeaders.Add(WellKnownHeaders.ContentLength, ContentLengthCache.GetValue(body.Length));

        if (options.WriteDateHeader && !_reusableHeaders.Contains(WellKnownHeaders.Date))
        {
            _reusableHeaders.Add(WellKnownHeaders.Date, DateHeaderCache.GetValue());
        }

        HeaderBlockWriter.Write(ref writer, _reusableHeaders);

        if (body.Length > 0)
        {
            writer.WriteBytes(body);
        }

        return writer.BytesWritten;
    }
}