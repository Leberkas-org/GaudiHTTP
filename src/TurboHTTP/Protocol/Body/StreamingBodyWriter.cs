using System.Buffers;

namespace TurboHTTP.Protocol.Body;

internal sealed class StreamingBodyWriter : IBodyWriter
{
    private IFramingEncoder? _framing;
    private Func<IMemoryOwner<byte>, ReadOnlyMemory<byte>, ValueTask>? _send;
    private IMemoryOwner<byte>? _rental;
    private int _written;

    public void Reset(IFramingEncoder framing, Func<IMemoryOwner<byte>, ReadOnlyMemory<byte>, ValueTask> send)
    {
        _framing = framing;
        _send = send;
        _rental?.Dispose();
        _rental = null;
        _written = 0;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        var size = Math.Max(sizeHint, 4 * 1024);
        var totalSize = _framing!.Headroom + size + _framing.Trailer;
        _rental = MemoryPool<byte>.Shared.Rent(totalSize);
        _written = 0;
        return _rental.Memory.Slice(_framing.Headroom, size);
    }

    public void Advance(int bytes)
    {
        _written += bytes;
    }

    public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
    {
        var framed = _framing!.Frame(_rental!, _framing.Headroom, _written);
        var owner = _rental!;
        _rental = null;
        _written = 0;
        _send!(owner, framed);
        return ValueTask.FromResult(new FlushResult(isCompleted: false));
    }

    public ValueTask CompleteAsync(CancellationToken ct = default)
    {
        var terminator = _framing!.GetTerminator();
        if (terminator.IsEmpty)
        {
            return default;
        }

        _send!(terminator.Owner, terminator.Memory);
        return default;
    }

    public void Dispose()
    {
        _rental?.Dispose();
        _rental = null;
    }
}
