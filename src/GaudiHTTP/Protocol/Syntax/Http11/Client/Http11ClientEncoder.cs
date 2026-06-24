using GaudiHTTP.Protocol.LineBased;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http11.Options;

namespace GaudiHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientEncoder(Http11ClientEncoderOptions options)
{
    private readonly HeaderCollection _reusableHeaders = new();
    private string? _preparedTarget;

    /// <summary>
    /// Builds the request headers once into the reusable collection and returns the EXACT number of
    /// bytes the request line + header block will occupy. The body is streamed separately, so it is
    /// excluded from this size. Pair 1:1 with <see cref="WriteTo"/>, which emits those bytes into a
    /// buffer sized by this value. Splitting build-then-size from write lets the caller rent an
    /// exactly-sized buffer with no throwaway header build for sizing and no body-sized over-rent.
    /// </summary>
    public int Prepare(HttpRequestMessage request, out Stream? bodyStream, out long? contentLength)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        RequestValidator.Validate(request);

        contentLength = request.Content?.Headers.ContentLength;
        bodyStream = request.Content?.ReadAsStream();

        _preparedTarget = request.ResolveTarget();
        HeaderBuilder.Build(request, options, _reusableHeaders);

        var requestLineSize = request.Method.Method.Length + 1 + _preparedTarget.Length + 1
                              + MessageVersionCodec.ToWireFormat(request.Version).Length + 2;
        return requestLineSize + _reusableHeaders.WireSize();
    }

    /// <summary>
    /// Writes the request line + header block prepared by the immediately preceding <see cref="Prepare"/>
    /// call into <paramref name="destination"/> (which must be at least the size Prepare returned).
    /// </summary>
    public int WriteTo(Span<byte> destination, HttpRequestMessage request)
    {
        var writer = SpanWriter.Create(destination);
        RequestLineWriter.Write(ref writer, request.Method.Method, _preparedTarget!, request.Version);
        HeaderBlockWriter.Write(ref writer, _reusableHeaders);

        return writer.BytesWritten;
    }

    public int Encode(Span<byte> destination, HttpRequestMessage request, out Stream? bodyStream, out long? contentLength)
    {
        Prepare(request, out bodyStream, out contentLength);
        return WriteTo(destination, request);
    }
}