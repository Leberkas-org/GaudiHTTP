using System.Globalization;
using System.Net;
using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientEncoder
{
    public int Encode(Span<byte> destination, HttpRequestMessage request, out Stream? bodyStream)
    {
        if (request.Content is null)
        {
            bodyStream = null;
            return EncodeHeadersOnly(destination, request, contentLength: 0);
        }

        // HTTP/1.0 always defers — need body bytes before Content-Length header can be written
        bodyStream = request.Content.ReadAsStream();
        return 0;
    }

    public int EncodeDeferred(Span<byte> destination, HttpRequestMessage request, ReadOnlySpan<byte> body)
    {
        var writer = SpanWriter.Create(destination);
        var targetStr = request.ResolveTarget();
        RequestLineWriter.Write(ref writer, request.Method.Method, targetStr, HttpVersion.Version10);

        var headers = request.GetHeaderCollection();
        headers.Add(WellKnownHeaders.ContentLength, body.Length.ToString(CultureInfo.InvariantCulture));
        HeaderBlockWriter.Write(ref writer, headers);

        if (body.Length > 0)
        {
            writer.WriteBytes(body);
        }

        return writer.BytesWritten;
    }

    private int EncodeHeadersOnly(Span<byte> destination, HttpRequestMessage request, int contentLength)
    {
        var writer = SpanWriter.Create(destination);
        var targetStr = request.ResolveTarget();
        RequestLineWriter.Write(ref writer, request.Method.Method, targetStr, HttpVersion.Version10);
        var headers = request.GetHeaderCollection();
        headers.Add(WellKnownHeaders.ContentLength, contentLength.ToString(CultureInfo.InvariantCulture));
        HeaderBlockWriter.Write(ref writer, headers);
        return writer.BytesWritten;
    }
}
