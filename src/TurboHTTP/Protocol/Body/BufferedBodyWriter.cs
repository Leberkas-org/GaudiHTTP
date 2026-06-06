using System.Buffers;

namespace TurboHTTP.Protocol.Body;

internal sealed class BufferedBodyWriter : IBodyWriter
{
    private IMemoryOwner<byte>? _owner;
    private int _written;
    private Action<IMemoryOwner<byte>, int>? _onComplete;

    public void Reset(Action<IMemoryOwner<byte>, int> onComplete)
    {
        _owner?.Dispose();
        _owner = MemoryPool<byte>.Shared.Rent(4 * 1024);
        _written = 0;
        _onComplete = onComplete;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        var needed = _written + Math.Max(sizeHint, 4 * 1024);
        EnsureCapacity(needed);
        return _owner!.Memory[_written..];
    }

    public void Advance(int bytes)
    {
        _written += bytes;
    }

    public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
        => ValueTask.FromResult(new FlushResult(false));

    public ValueTask CompleteAsync(CancellationToken ct = default)
    {
        var owner = _owner!;
        var written = _written;
        _owner = null;
        _written = 0;
        _onComplete!(owner, written);
        return default;
    }

    private void EnsureCapacity(int needed)
    {
        if (_owner is not null && _owner.Memory.Length >= needed)
        {
            return;
        }

        var newSize = Math.Max(needed, (_owner?.Memory.Length ?? 4 * 1024) * 2);
        var next = MemoryPool<byte>.Shared.Rent(newSize);
        if (_owner is not null && _written > 0)
        {
            _owner.Memory[.._written].CopyTo(next.Memory);
        }

        _owner?.Dispose();
        _owner = next;
    }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
    }
}
