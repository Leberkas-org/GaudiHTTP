using System.Buffers;
using System.Net;

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

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
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
