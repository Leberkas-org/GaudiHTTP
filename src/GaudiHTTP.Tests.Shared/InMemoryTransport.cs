using System.Buffers;
using System.IO.Pipelines;
using Servus.Akka.Transport;

namespace GaudiHTTP.Tests.Shared;

public enum FlushMode
{
    Sync,
    Async,
    SyncCompleted
}

internal sealed class InMemoryTransport : IConnectionTransport
{
    private readonly Queue<TaskCompletionSource<ReadResult>> _pendingReads = new();
    private readonly Queue<ReadResult> _bufferedResults = new();
    private readonly ArrayBufferWriter<byte> _writer = new(4 * 1024);

    public FlushMode FlushMode { get; set; } = FlushMode.Sync;
    public int ReadCount { get; private set; }
    public int FlushCount { get; private set; }
    public int AdvanceToCount { get; private set; }
    public bool OutputCompleted { get; private set; }
    public bool Aborted { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _writer.WrittenSpan;
    public ReadOnlyMemory<byte> WrittenMemory => _writer.WrittenMemory;
    public int WrittenCount => _writer.WrittenCount;

    public byte[] TakeWrittenBytes()
    {
        var result = _writer.WrittenSpan.ToArray();
        _writer.ResetWrittenCount();
        return result;
    }

    public void Feed(byte[] data)
    {
        var result = new ReadResult(
            new ReadOnlySequence<byte>(data), isCanceled: false, isCompleted: false);

        if (_pendingReads.Count > 0)
        {
            _pendingReads.Dequeue().SetResult(result);
        }
        else
        {
            _bufferedResults.Enqueue(result);
        }
    }

    public void Feed(ReadOnlySequence<byte> data)
    {
        var result = new ReadResult(data, isCanceled: false, isCompleted: false);

        if (_pendingReads.Count > 0)
        {
            _pendingReads.Dequeue().SetResult(result);
        }
        else
        {
            _bufferedResults.Enqueue(result);
        }
    }

    public void Complete()
    {
        var result = new ReadResult(
            ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);

        if (_pendingReads.Count > 0)
        {
            _pendingReads.Dequeue().SetResult(result);
        }
        else
        {
            _bufferedResults.Enqueue(result);
        }
    }

    public ValueTask<ReadResult> ReadAsync(CancellationToken ct = default)
    {
        ReadCount++;

        if (_bufferedResults.Count > 0)
        {
            return new ValueTask<ReadResult>(_bufferedResults.Dequeue());
        }

        var tcs = new TaskCompletionSource<ReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReads.Enqueue(tcs);
        return new ValueTask<ReadResult>(tcs.Task);
    }

    public void AdvanceTo(SequencePosition consumed)
    {
        AdvanceToCount++;
    }

    public void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        AdvanceToCount++;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        return _writer.GetMemory(sizeHint);
    }

    public void Advance(int bytes)
    {
        _writer.Advance(bytes);
    }

    public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
    {
        FlushCount++;
        return FlushMode switch
        {
            FlushMode.Sync => new ValueTask<FlushResult>(new FlushResult(false, false)),
            FlushMode.SyncCompleted => new ValueTask<FlushResult>(new FlushResult(false, true)),
            FlushMode.Async => new ValueTask<FlushResult>(
                new TaskCompletionSource<FlushResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task),
            _ => throw new InvalidOperationException($"Unknown FlushMode: {FlushMode}")
        };
    }

    public void CompleteOutput()
    {
        OutputCompleted = true;
    }

    public ConnectionInfo Info => ConnectionInfo.None;

    public void Abort()
    {
        Aborted = true;
    }
}
