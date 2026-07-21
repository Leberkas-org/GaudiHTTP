using System.Buffers;
using System.Globalization;
using System.Net;
using GaudiHTTP.Protocol.LineBased;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientEncoder
{
    public int Encode(IBufferWriter<byte> writer, HttpRequestMessage request, out Stream? bodyStream)
    {
        if (request.Content is null)
        {
            bodyStream = null;
            return EncodeHeadersOnly(writer, request, contentLength: 0);
        }

        bodyStream = request.Content.ReadAsStream();
        return 0;
    }

    public int EncodeDeferred(IBufferWriter<byte> writer, HttpRequestMessage request, ReadOnlySpan<byte> body)
    {
        var targetStr = request.ResolveTarget();
        var headers = request.GetHeaderCollection();
        headers.Add(WellKnownHeaders.ContentLength, body.Length.ToString(CultureInfo.InvariantCulture));

        var headerSize = request.Method.Method.Length + 1 + targetStr.Length + 1
                         + MessageVersionCodec.ToWireFormat(HttpVersion.Version10).Length + 2
                         + headers.WireSize() + body.Length;

        var span = writer.GetSpan(headerSize);
        var sw = SpanWriter.Create(span);
        RequestLineWriter.Write(ref sw, request.Method.Method, targetStr, HttpVersion.Version10);
        HeaderBlockWriter.Write(ref sw, headers);

        if (body.Length > 0)
        {
            sw.WriteBytes(body);
        }

        writer.Advance(sw.BytesWritten);
        return sw.BytesWritten;
    }

    internal int EncodeHeadersOnly(IBufferWriter<byte> writer, HttpRequestMessage request, long contentLength)
    {
        var targetStr = request.ResolveTarget();
        var headers = request.GetHeaderCollection();
        headers.Add(WellKnownHeaders.ContentLength, contentLength.ToString(CultureInfo.InvariantCulture));

        var headerSize = request.Method.Method.Length + 1 + targetStr.Length + 1
                         + MessageVersionCodec.ToWireFormat(HttpVersion.Version10).Length + 2
                         + headers.WireSize();

        var span = writer.GetSpan(headerSize);
        var sw = SpanWriter.Create(span);
        RequestLineWriter.Write(ref sw, request.Method.Method, targetStr, HttpVersion.Version10);
        HeaderBlockWriter.Write(ref sw, headers);

        writer.Advance(sw.BytesWritten);
        return sw.BytesWritten;
    }
}
