using System.Globalization;
using Akka.Actor;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Syntax.Http10.Options;

namespace TurboHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientEncoder
{
    private readonly Http10ClientEncoderOptions _options;
    private readonly Http10Profile _profile;

    public Http10ClientEncoder(Http10ClientEncoderOptions options, Http10Profile profile)
    {
        options.Validate();
        _options = options;
        _profile = profile;
    }

    public int Encode(Span<byte> destination, HttpRequestMessage request, IActorRef stageActor)
    {
        if (request.Content is null)
        {
            return EncodeHeadersOnly(destination, request, contentLength: 0);
        }

        // HTTP/1.0 always defers — need body bytes before Content-Length header can be written
        var bodyEncoder = new ContentLengthBufferedBodyEncoder();
        bodyEncoder.Start(request.Content, stageActor);
        return 0;
    }

    public int EncodeDeferred(Span<byte> destination, HttpRequestMessage request, ReadOnlySpan<byte> body)
    {
        var writer = SpanWriter.Create(destination);
        var targetStr = request.ResolveTarget();
        RequestLineWriter.Write(ref writer, request.Method.Method, targetStr, _profile.Version);

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
        RequestLineWriter.Write(ref writer, request.Method.Method, targetStr, _profile.Version);
        var headers = request.GetHeaderCollection();
        headers.Add(WellKnownHeaders.ContentLength, contentLength.ToString(CultureInfo.InvariantCulture));
        HeaderBlockWriter.Write(ref writer, request.GetHeaderCollection());
        return writer.BytesWritten;
    }
}