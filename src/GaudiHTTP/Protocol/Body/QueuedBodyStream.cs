namespace TurboHTTP.Protocol.Body;

internal sealed class QueuedBodyStream(QueuedBodyReader reader) : Stream
{
    private ReadOnlyMemory<byte> _current;
    private int _offset;
    private bool _done;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_done)
        {
            return 0;
        }

        if (_current.IsEmpty)
        {
            var result = ReadNextSegment();
            if (result is { IsCompleted: true, Memory.IsEmpty: true })
            {
                _done = true;
                return 0;
            }

            _current = result.Memory;
            _offset = 0;
        }

        return CopyFromCurrent(buffer);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_done)
        {
            return 0;
        }

        if (_current.IsEmpty)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (result is { IsCompleted: true, Memory.IsEmpty: true })
            {
                _done = true;
                return 0;
            }

            _current = result.Memory;
            _offset = 0;
        }

        return CopyFromCurrent(buffer.Span);
    }

    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (_done)
        {
            return;
        }

        // Finish any partially-read chunk from a prior Read/ReadAsync before draining the reader.
        // The reader still owns this chunk's rental (AdvanceTo runs only once it is fully consumed),
        // so release it after the write completes.
        if (!_current.IsEmpty)
        {
            await destination.WriteAsync(_current[_offset..], cancellationToken).ConfigureAwait(false);
            _current = default;
            _offset = 0;
            reader.AdvanceTo();
        }

        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsCompleted)
            {
                _done = true;
                return;
            }

            // Write the pooled chunk straight to the destination — no per-read copy into an
            // 81920-byte framework rental. AdvanceTo (which returns the rental to the pool) runs
            // only AFTER the write await completes, so the buffer is never recycled while in use.
            if (!result.Memory.IsEmpty)
            {
                await destination.WriteAsync(result.Memory, cancellationToken).ConfigureAwait(false);
            }

            reader.AdvanceTo();
        }
    }

    private int CopyFromCurrent(Span<byte> destination)
    {
        var available = _current.Length - _offset;
        var toCopy = Math.Min(available, destination.Length);
        _current.Span.Slice(_offset, toCopy).CopyTo(destination);
        _offset += toCopy;

        if (_offset >= _current.Length)
        {
            _current = default;
            _offset = 0;
            reader.AdvanceTo();
        }

        return toCopy;
    }

    private BodyReadResult ReadNextSegment()
    {
        var vt = reader.ReadAsync(CancellationToken.None);
        if (!vt.IsCompleted)
        {
            throw new InvalidOperationException(
                "QueuedBodyReader.ReadAsync not completed synchronously — use ReadAsync on the stream.");
        }

        return vt.Result;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
