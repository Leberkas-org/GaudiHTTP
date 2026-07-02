using System.Buffers;
using GaudiHTTP.Pooling;
using Servus.Akka.Transport;

namespace GaudiHTTP.Protocol.Body;

internal sealed class BufferedBodyReader : Poolable<BufferedBodyReader>, IBufferedBodyReader
{
    private IMemoryOwner<byte>? _owner;
    private int _expected;
    private int _received;
    private bool _openEnded;

    public bool IsBuffered => true;
    public bool IsCompleted { get; private set; }
    public bool IsOpenEnded => _openEnded;

    public void Reset(int contentLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        _owner?.Dispose();
        _expected = contentLength;
        _openEnded = false;
        _received = 0;
        IsCompleted = contentLength == 0;
        _owner = contentLength > 0
            ? PooledArrayMemoryOwner.Create(contentLength)
            : null;
    }

    public void ResetOpenEnded()
    {
        _owner?.Dispose();
        _expected = 0;
        _openEnded = true;
        _received = 0;
        IsCompleted = false;
        _owner = PooledArrayMemoryOwner.Create(4 * 1024);
    }

    protected override void OnReset() => ResetOpenEnded();

    public void MarkComplete()
    {
        IsCompleted = true;
    }

    public int Feed(ReadOnlySpan<byte> data)
    {
        if (_openEnded)
        {
            if (data.IsEmpty)
            {
                return 0;
            }

            EnsureCapacity(_received + data.Length);
            data.CopyTo(_owner!.Memory.Span[_received..]);
            _received += data.Length;
            return data.Length;
        }

        var take = Math.Min(_expected - _received, data.Length);
        if (take > 0)
        {
            data[..take].CopyTo(_owner!.Memory.Span[_received..]);
            _received += take;
        }

        IsCompleted = _received == _expected;
        return take;
    }

    private void EnsureCapacity(int needed)
    {
        if (_owner is not null && _owner.Memory.Length >= needed)
        {
            return;
        }

        var newSize = Math.Max(needed, (_owner?.Memory.Length ?? 4 * 1024) * 2);
        var next = PooledArrayMemoryOwner.Create(newSize);
        if (_owner is not null && _received > 0)
        {
            _owner.Memory[.._received].CopyTo(next.Memory);
        }

        _owner?.Dispose();
        _owner = next;
    }

    public ReadOnlyMemory<byte> GetBody()
        => _owner?.Memory[.._received] ?? ReadOnlyMemory<byte>.Empty;

    public Stream AsStream()
        => _owner is not null
            ? new PooledMemoryStream(_owner, _received)
            : Stream.Null;

    private sealed class PooledMemoryStream(IMemoryOwner<byte> owner, int length) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var available = length - _position;
            if (available <= 0)
            {
                return 0;
            }

            var toCopy = Math.Min(count, available);
            owner.Memory.Span.Slice(_position, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
            _position += toCopy;
            return toCopy;
        }

        public override int Read(Span<byte> buffer)
        {
            var available = length - _position;
            if (available <= 0)
            {
                return 0;
            }

            var toCopy = Math.Min(buffer.Length, available);
            owner.Memory.Span.Slice(_position, toCopy).CopyTo(buffer[..toCopy]);
            _position += toCopy;
            return toCopy;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => _position + (int)offset,
                SeekOrigin.End => length + (int)offset,
                _ => _position
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                owner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
