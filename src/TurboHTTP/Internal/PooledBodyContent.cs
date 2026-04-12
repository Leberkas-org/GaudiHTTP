using System.Buffers;
using System.Net;

namespace TurboHTTP.Internal;

/// <summary>
/// An <see cref="HttpContent"/> backed by a pooled <see cref="IMemoryOwner{T}"/>.
/// Writes directly from the rented memory without copying. The memory is returned
/// to the pool when the content (and therefore the owning <see cref="HttpResponseMessage"/>)
/// is disposed.
/// </summary>
internal sealed class PooledBodyContent : HttpContent
{
    private IMemoryOwner<byte>? _owner;
    private readonly int _length;

    public PooledBodyContent(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        _length = length;
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
        => stream.Write(_owner!.Memory.Span[.._length]);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await stream.WriteAsync(_owner!.Memory[.._length]);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(_owner!.Memory[.._length], cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// An <see cref="HttpContent"/> that holds pooled <see cref="NetworkBuffer"/> chunks
/// accumulated during connection-close-delimited body streaming.
/// </summary>
internal sealed class PooledChunksContent : HttpContent
{
    private readonly byte[]? _initial;
    private readonly List<NetworkBuffer>? _chunks;

    public PooledChunksContent(byte[]? initial, List<NetworkBuffer>? chunks)
    {
        _initial = initial;
        _chunks = chunks;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken ct)
    {
        if (_initial is { Length: > 0 })
        {
            await stream.WriteAsync(_initial, ct).ConfigureAwait(false);
        }

        if (_chunks is not null)
        {
            foreach (var buf in _chunks)
            {
                await stream.WriteAsync(buf.Memory, ct).ConfigureAwait(false);
            }
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _initial?.Length ?? 0;
        if (_chunks is not null)
        {
            foreach (var buf in _chunks)
            {
                length += buf.Length;
            }
        }

        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _chunks is not null)
        {
            foreach (var buf in _chunks)
            {
                buf.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}