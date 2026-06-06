using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientEncoder(Http11ClientEncoderOptions options)
{
    private readonly HeaderCollection _reusableHeaders = new();

    public int Encode(Span<byte> destination, HttpRequestMessage request, out Stream? bodyStream, out long? contentLength)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        RequestValidator.Validate(request);

        contentLength = request.Content?.Headers.ContentLength;
        bodyStream = request.Content?.ReadAsStream();

        var writer = SpanWriter.Create(destination);
        var targetStr = request.ResolveTarget();
        RequestLineWriter.Write(ref writer, request.Method.Method, targetStr, request.Version);
        HeaderBuilder.Build(request, options, _reusableHeaders);
        HeaderBlockWriter.Write(ref writer, _reusableHeaders);

        return writer.BytesWritten;
    }
}