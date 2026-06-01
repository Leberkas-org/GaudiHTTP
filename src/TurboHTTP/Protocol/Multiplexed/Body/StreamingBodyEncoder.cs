using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class StreamingBodyEncoder(int chunkSize) : IPausableBodyEncoder
{
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private TaskCompletionSource? _resumeSignal;

    public void Start(Stream bodyStream, Action<object> onMessage) => _ = DrainAsync(bodyStream, onMessage, _cts.Token);

    public void Pause()
    {
        lock (_gate)
        {
            _resumeSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _resumeSignal?.TrySetResult();
            _resumeSignal = null;
        }
    }

    private async Task DrainAsync(Stream stream, Action<object> onMessage, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                Task? resume;
                lock (_gate)
                {
                    resume = _resumeSignal?.Task;
                }

                if (resume is not null)
                {
                    await resume.WaitAsync(ct).ConfigureAwait(false);
                }

                var owner = MemoryPool<byte>.Shared.Rent(chunkSize);
                var bytesRead = await stream.ReadAsync(owner.Memory[..chunkSize], ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    owner.Dispose();
                    break;
                }

                onMessage(new OutboundBodyChunk(owner, bytesRead));
            }

            onMessage(new OutboundBodyComplete());
        }
        catch (Exception ex)
        {
            onMessage(new OutboundBodyFailed(ex));
        }
    }

    public void Dispose()
    {
        // Release a paused drain loop so it can observe cancellation instead of hanging.
        Resume();
        _cts.Cancel();
        _cts.Dispose();
    }
}
