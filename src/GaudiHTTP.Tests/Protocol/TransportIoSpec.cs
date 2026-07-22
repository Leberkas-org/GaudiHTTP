using System.Buffers;
using System.IO.Pipelines;
using Akka.Actor;
using GaudiHTTP.Protocol;
using Servus.Akka.Transport;

namespace GaudiHTTP.Tests.Protocol;

public sealed class TransportIoSpec
{
    private readonly List<ReadOnlySequence<byte>> _decodedData = [];
    private readonly List<Exception?> _transportLost = [];
    private readonly List<int> _flushCompleted = [];
    private bool _shouldPause;
    private readonly TransportIo _tio;

    public TransportIoSpec()
    {
        var self = new NoopActorRef();
        _tio = new TransportIo(
            self: () => self,
            shouldPause: () => _shouldPause,
            decode: DecodeData,
            onFlushCompleted: () => _flushCompleted.Add(1),
            onFlushDeferred: () => { },
            onTransportLost: ex => _transportLost.Add(ex));
    }

    private (SequencePosition Consumed, SequencePosition Examined) DecodeData(ReadOnlySequence<byte> data)
    {
        _decodedData.Add(data);
        return (data.End, data.End);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Sync_fast_path_should_process_multiple_reads_without_PipeTo()
    {
        var transport = new ScriptableTransport();
        // First read goes through PipeTo (budget=0), so use a pending read
        transport.EnqueuePendingRead();

        _tio.OnConnected(transport);
        _tio.RequestRead();
        Assert.Empty(_decodedData);
        Assert.Equal(1, transport.ReadCount);

        // Simulate async delivery of the first read result
        transport.EnqueueSyncRead(new byte[] { 2, 3 });
        transport.EnqueueSyncRead(new byte[] { 4, 5 });
        transport.EnqueuePendingRead(); // terminal

        var gen = _tio.TransportGen;
        Assert.True(_tio.OnAsyncResult(new ReadCompleted(MakeReadResult(new byte[] { 1 }), gen)));

        // The OnAsyncResult decoded [1], then sync fast-path decoded [2,3] and [4,5]
        Assert.Equal(3, _decodedData.Count);
        Assert.Equal(new byte[] { 1 }, _decodedData[0].ToArray());
        Assert.Equal(new byte[] { 2, 3 }, _decodedData[1].ToArray());
        Assert.Equal(new byte[] { 4, 5 }, _decodedData[2].ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Async_path_should_bridge_via_PipeTo_and_generation_guard_drops_stale()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();

        _tio.OnConnected(transport);
        _tio.RequestRead();
        Assert.Empty(_decodedData);

        var gen = _tio.TransportGen;
        transport.EnqueuePendingRead();
        Assert.True(_tio.OnAsyncResult(new ReadCompleted(MakeReadResult(new byte[] { 10, 20 }), gen)));
        Assert.Single(_decodedData);
        Assert.Equal(new byte[] { 10, 20 }, _decodedData[0].ToArray());

        // Reconnect — new generation
        _tio.OnConnected(null);
        var staleRead = new ReadCompleted(MakeReadResult(new byte[] { 99 }), gen);
        Assert.True(_tio.OnAsyncResult(staleRead));
        Assert.Single(_decodedData);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void ShouldPauseNetwork_should_prevent_RequestRead()
    {
        var transport = new ScriptableTransport();

        _shouldPause = true;
        transport.EnqueuePendingRead();
        _tio.OnConnected(transport);

        Assert.Empty(_decodedData);
        Assert.Equal(0, transport.ReadCount);

        _shouldPause = false;
        _tio.RequestRead();

        Assert.Equal(1, transport.ReadCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void BodyResumed_should_rearm_read_after_pause()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        _tio.OnConnected(transport);
        _tio.RequestRead();
        Assert.Equal(1, transport.ReadCount);

        // Simulate async read completion, but next read would be paused
        _shouldPause = true;
        transport.EnqueuePendingRead();

        var gen = _tio.TransportGen;
        Assert.True(_tio.OnAsyncResult(new ReadCompleted(MakeReadResult(new byte[] { 1 }), gen)));

        // Read was decoded but next read was not started due to pause
        Assert.Single(_decodedData);
        Assert.Equal(1, transport.ReadCount); // still 1, no new read

        // Resume
        _shouldPause = false;
        _tio.RequestRead();
        Assert.Equal(2, transport.ReadCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void RequestRead_should_be_noop_when_transport_is_null()
    {
        _tio.OnConnected(null);
        _tio.RequestRead();

        Assert.Empty(_decodedData);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void RequestFlush_should_be_noop_when_transport_is_null()
    {
        _tio.OnConnected(null);
        _tio.RequestFlush();

        Assert.Empty(_flushCompleted);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void TransportGen_should_increment_on_every_TransportConnected()
    {
        var gen0 = _tio.TransportGen;

        _tio.OnConnected(null);
        var gen1 = _tio.TransportGen;
        Assert.Equal(gen0 + 1, gen1);

        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        _tio.OnConnected(transport);
        var gen2 = _tio.TransportGen;
        Assert.Equal(gen1 + 1, gen2);

        _tio.OnDisconnected();
        var gen3 = _tio.TransportGen;
        Assert.Equal(gen2 + 1, gen3);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Sync_budget_should_prevent_actor_starvation()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        _tio.OnConnected(transport);
        _tio.RequestRead();

        // After first async dispatch, budget = MaxSyncReads
        for (var i = 0; i < TransportIo.MaxSyncReads + 3; i++)
        {
            transport.EnqueueSyncRead(new byte[] { (byte)(i + 1) });
        }
        transport.EnqueuePendingRead(); // terminal for the budget-exceeded PipeTo

        var gen = _tio.TransportGen;
        Assert.True(_tio.OnAsyncResult(new ReadCompleted(MakeReadResult(new byte[] { 0 }), gen)));

        // 1 from OnAsyncResult + MaxSyncReads sync reads = MaxSyncReads + 1
        Assert.Equal(1 + TransportIo.MaxSyncReads, _decodedData.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Flush_sync_fast_path_should_signal_completion()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        transport.FlushMode = FlushMode.Sync;

        _tio.OnConnected(transport);
        _tio.RequestFlush();

        Assert.Single(_flushCompleted);
        Assert.False(_tio.IsFlushInProgress);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Flush_async_path_should_set_FlushInProgress()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        transport.FlushMode = FlushMode.Async;

        _tio.OnConnected(transport);
        _tio.RequestFlush();

        Assert.True(_tio.IsFlushInProgress);
        Assert.Empty(_flushCompleted);

        var fc = new FlushCompleted(new FlushResult(false, false), _tio.TransportGen);
        Assert.True(_tio.OnAsyncResult(fc));

        Assert.False(_tio.IsFlushInProgress);
        Assert.Single(_flushCompleted);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Flush_IsCompleted_should_signal_transport_loss()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();
        transport.FlushMode = FlushMode.SyncCompleted;

        _tio.OnConnected(transport);
        _tio.RequestFlush();

        Assert.Single(_transportLost);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void ReadFailed_should_signal_transport_loss()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();

        _tio.OnConnected(transport);

        var ex = new IOException("connection reset");
        var rf = new ReadFailed(ex, _tio.TransportGen);
        Assert.True(_tio.OnAsyncResult(rf));

        Assert.Single(_transportLost);
        Assert.Same(ex, _transportLost[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Unknown_message_should_not_be_handled()
    {
        Assert.False(_tio.OnAsyncResult("some body message"));
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "PipeIO")]
    public void Empty_completed_read_should_signal_connection_close()
    {
        var transport = new ScriptableTransport();
        transport.EnqueuePendingRead();

        _tio.OnConnected(transport);

        var gen = _tio.TransportGen;
        var rc = new ReadCompleted(
            new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true), gen);
        Assert.True(_tio.OnAsyncResult(rc));

        Assert.Empty(_decodedData);
        Assert.Single(_transportLost);
        Assert.Null(_transportLost[0]);
    }

    private static ReadResult MakeReadResult(byte[] data)
    {
        return new ReadResult(new ReadOnlySequence<byte>(data), isCanceled: false, isCompleted: false);
    }

    private sealed class NoopActorRef : MinimalActorRef
    {
        public override ActorPath Path { get; } = new RootActorPath(new Address("akka", "test")) / "transport-io-test";
        public override IActorRefProvider Provider => throw new NotSupportedException();
        protected override void TellInternal(object message, IActorRef sender) { }
    }
}

internal enum FlushMode
{
    Sync,
    Async,
    SyncCompleted
}

internal sealed class ScriptableTransport : IConnectionTransport
{
    private readonly Queue<Func<ValueTask<ReadResult>>> _reads = new();
    public FlushMode FlushMode { get; set; } = FlushMode.Sync;
    public int ReadCount { get; private set; }

    public void EnqueueSyncRead(byte[] data)
    {
        var result = new ReadResult(new ReadOnlySequence<byte>(data), isCanceled: false, isCompleted: false);
        _reads.Enqueue(() => new ValueTask<ReadResult>(result));
    }

    public void EnqueuePendingRead()
    {
        var tcs = new TaskCompletionSource<ReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reads.Enqueue(() => new ValueTask<ReadResult>(tcs.Task));
    }

    public ValueTask<ReadResult> ReadAsync(CancellationToken ct = default)
    {
        ReadCount++;
        if (_reads.Count == 0)
        {
            return new ValueTask<ReadResult>(Task.FromException<ReadResult>(
                new InvalidOperationException("No reads enqueued")));
        }

        return _reads.Dequeue()();
    }

    public void AdvanceTo(SequencePosition consumed) { }
    public void AdvanceTo(SequencePosition consumed, SequencePosition examined) { }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        var buffer = new byte[Math.Max(sizeHint, 4 * 1024)];
        return buffer;
    }

    public void Advance(int bytes) { }

    public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
    {
        return FlushMode switch
        {
            FlushMode.Sync => new ValueTask<FlushResult>(new FlushResult(false, false)),
            FlushMode.SyncCompleted => new ValueTask<FlushResult>(new FlushResult(false, true)),
            FlushMode.Async => new ValueTask<FlushResult>(
                new TaskCompletionSource<FlushResult>(TaskCreationOptions.RunContinuationsAsynchronously).Task),
            _ => throw new InvalidOperationException()
        };
    }

    public void CompleteOutput() { }
    public ConnectionInfo Info => ConnectionInfo.None;
    public void Abort() { }
}
