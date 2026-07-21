using System.Buffers;
using System.Net;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.LineBased;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http10.Options;

namespace GaudiHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerEncoder(Http10ServerEncoderOptions options)
{
    private readonly HeaderCollection _reusableHeaders = new();

    public int Encode(IBufferWriter<byte> _, IFeatureCollection features)
    {
        return 0;
    }

    public int EncodeDeferred(IBufferWriter<byte> writer, IFeatureCollection features, ReadOnlySpan<byte> body,
        bool suppressContentLength = false)
    {
        var responseFeature = features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;

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

        if (!suppressContentLength && !_reusableHeaders.Contains(WellKnownHeaders.ContentLength))
        {
            _reusableHeaders.Add(WellKnownHeaders.ContentLength, ContentLengthCache.GetValue(body.Length));
        }

        if (options.WriteDateHeader && !_reusableHeaders.Contains(WellKnownHeaders.Date))
        {
            _reusableHeaders.Add(WellKnownHeaders.Date, DateHeaderCache.GetValue());
        }

        var statusLineSize = MessageVersionCodec.ToWireFormat(HttpVersion.Version10).Length + 1 + 3 + 2;
        var headerSize = statusLineSize + _reusableHeaders.WireSize() + body.Length;
        var span = writer.GetSpan(headerSize);
        var sw = SpanWriter.Create(span);
        StatusLineWriter.Write(ref sw, HttpVersion.Version10, statusCode);
        HeaderBlockWriter.Write(ref sw, _reusableHeaders);

        if (body.Length > 0)
        {
            sw.WriteBytes(body);
        }

        writer.Advance(sw.BytesWritten);
        return sw.BytesWritten;
    }
}
