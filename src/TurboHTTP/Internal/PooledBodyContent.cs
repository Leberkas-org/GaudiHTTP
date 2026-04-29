using System.Buffers;
using System.Net;
using Servus.Akka.Transport;

namespace TurboHTTP.Internal;

internal sealed class PooledBodyContent : HttpContent
{
    private IMemoryOwner<byte>? _owner;
    private readonly int _length;

    public PooledBodyContent(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        _length = length;
    }

    public static PooledBodyContent FromChunks(byte[]? initial, List<TransportBuffer>? chunks)
    {
        var totalLength = initial?.Length ?? 0;
        if (chunks is not null)
        {
            foreach (var buf in chunks)
            {
                totalLength += buf.Length;
            }
        }

        var owner = MemoryPool<byte>.Shared.Rent(totalLength);
        var target = owner.Memory.Span;
        var offset = 0;

        if (initial is { Length: > 0 })
        {
            initial.CopyTo(target);
            offset += initial.Length;
        }

        if (chunks is not null)
        {
            foreach (var buf in chunks)
            {
                buf.Memory.Span.CopyTo(target[offset..]);
                offset += buf.Length;
                buf.Dispose();
            }
        }

        return new PooledBodyContent(owner, totalLength);
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        var mem = AcquireOwner();
        stream.Write(mem.Memory.Span[.._length]);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var mem = AcquireOwner();
        var vt = stream.WriteAsync(mem.Memory[.._length]);
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        var mem = AcquireOwner();
        var vt = stream.WriteAsync(mem.Memory[.._length], cancellationToken);
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
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
            var prev = Interlocked.Exchange(ref _owner, null);
            prev?.Dispose();
        }

        base.Dispose(disposing);
    }

    private IMemoryOwner<byte> AcquireOwner()
    {
        var mem = Interlocked.CompareExchange(ref _owner, null, null);
        ObjectDisposedException.ThrowIf(mem is null, this);
        return mem;
    }
}
