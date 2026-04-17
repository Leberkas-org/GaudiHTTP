using System.Net;

namespace TurboHTTP.Protocol.Caching;

/// <summary>
/// Non-owning <see cref="HttpContent"/> that serves a cached response body from
/// <see cref="ReadOnlyMemory{T}"/> without copying. The memory is owned by the
/// <see cref="CacheEntry"/> and must remain alive for the lifetime of this content.
/// </summary>
internal sealed class CachedBodyContent : HttpContent
{
    private readonly ReadOnlyMemory<byte> _body;

    public CachedBodyContent(ReadOnlyMemory<byte> body)
    {
        _body = body;
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
        => stream.Write(_body.Span);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => stream.WriteAsync(_body).AsTask();

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
        => stream.WriteAsync(_body, cancellationToken).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        return true;
    }
}
