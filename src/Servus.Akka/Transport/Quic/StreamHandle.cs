namespace Servus.Akka.Transport.Quic;

public sealed class StreamHandle : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly Action? _onWritesComplete;

    internal StreamHandle(Stream stream, Action? onWritesComplete)
    {
        _stream = stream;
        _onWritesComplete = onWritesComplete;
    }

    public ValueTask WriteAsync(TransportBuffer buffer)
    {
        var memory = buffer.Memory;
        var task = _stream.WriteAsync(memory);
        if (task.IsCompletedSuccessfully)
        {
            buffer.Dispose();
            return default;
        }

        return AwaitAndDispose(task, buffer);
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        return _stream.ReadAsync(buffer, ct);
    }

    public void CompleteWrites()
    {
        _onWritesComplete?.Invoke();
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private static async ValueTask AwaitAndDispose(ValueTask writeTask, TransportBuffer buffer)
    {
        try
        {
            await writeTask.ConfigureAwait(false);
        }
        finally
        {
            buffer.Dispose();
        }
    }
}
