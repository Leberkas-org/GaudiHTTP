namespace Servus.Akka.Tests.Utils;

public sealed class SlowCompletingWriteStream : Stream
{
    private readonly MemoryStream _inner = new();
    public int WrittenBytes { get; private set; }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        WrittenBytes = buffer.Length;
        // Return a pending ValueTask to force the slow path
        await Task.Delay(10, ct);
        await _inner.WriteAsync(buffer, ct);
    }
}