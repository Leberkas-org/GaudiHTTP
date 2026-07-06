using System.Buffers;
using GaudiHTTP.Pooling;
using Servus.Akka.Transport;

namespace GaudiHTTP.Protocol.Body;

internal sealed class BufferedBodyReader : Poolable<BufferedBodyReader>, IBufferedBodyReader
{
    private PooledArrayMemoryOwner? _owner;
    private int _expected;
    private int _received;

    public bool IsBuffered => true;
    public bool IsCompleted { get; private set; }
    public bool IsOpenEnded { get; private set; }

    public void Reset(int contentLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        _expected = contentLength;
        IsOpenEnded = false;
        _received = 0;
        IsCompleted = contentLength == 0;

        if (contentLength > 0 && (_owner is null || _owner.Memory.Length < contentLength))
        {
            _owner?.Dispose();
            _owner = PooledArrayMemoryOwner.Create(contentLength);
        }
    }

    public void ResetOpenEnded()
    {
        _expected = 0;
        IsOpenEnded = true;
        _received = 0;
        IsCompleted = false;

        if (_owner is null || _owner.Memory.Length < 4 * 1024)
        {
            _owner?.Dispose();
            _owner = PooledArrayMemoryOwner.Create(4 * 1024);
        }
    }

    protected override void OnReset()
    {
        _expected = 0;
        IsOpenEnded = false;
        _received = 0;
        IsCompleted = false;
        if (_owner is not null && _owner.Memory.Length > 1024 * 1024)
        {
            _owner.Dispose();
            _owner = null;
        }
    }

    public void MarkComplete()
    {
        IsCompleted = true;
    }

    public int Feed(ReadOnlySpan<byte> data)
    {
        if (IsOpenEnded)
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

    public Stream AsStream() => AsOwningStream();

    public Stream AsOwningStream()
    {
        if (_owner is null)
        {
            return Stream.Null;
        }

        var stream = new PooledMemoryStream(_owner.Memory[.._received], _owner);
        _owner = null;
        return stream;
    }

    private sealed class PooledMemoryStream(ReadOnlyMemory<byte> memory, IMemoryOwner<byte>? ownedOwner) : Stream
    {
        private IMemoryOwner<byte>? _ownedOwner = ownedOwner;
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => memory.Length;

        public override long Position
        {
            get => _position;
            set => _position = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var available = memory.Length - _position;
            if (available <= 0)
            {
                return 0;
            }

            var toCopy = Math.Min(count, available);
            memory.Span.Slice(_position, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
            _position += toCopy;
            return toCopy;
        }

        public override int Read(Span<byte> buffer)
        {
            var available = memory.Length - _position;
            if (available <= 0)
            {
                return 0;
            }

            var toCopy = Math.Min(buffer.Length, available);
            memory.Span.Slice(_position, toCopy).CopyTo(buffer[..toCopy]);
            _position += toCopy;
            return toCopy;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => _position + (int)offset,
                SeekOrigin.End => memory.Length + (int)offset,
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
                // Streams MUST tolerate multiple Dispose calls (Close + DisposeAsync + a wrapping
                // decompressor that owns its source all dispose the same instance). The owner's
                // wrapper is pool-recycled on its first Dispose, so a second Dispose through a
                // retained reference would dispose the wrapper's NEW renter's array — the exact
                // shared-pool poisoning behind the sporadic H2 receive-path corruption. Interlocked:
                // the decompressor's dispose (thread pool) can race the consumer's dispose.
                Interlocked.Exchange(ref _ownedOwner, null)?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
