using System.Buffers;
using System.Threading.Tasks.Sources;

namespace TurboHTTP.Protocol.Body;

internal sealed class QueuedBodyReader : IStreamingBodyReader, IValueTaskSource<BodyReadResult>
{
    // This reader is a true cross-thread boundary: the connection-stage (actor) thread
    // produces via TryEnqueue/Complete/Fault while the application thread consumes via
    // ReadAsync/AdvanceTo. All mutable state is guarded by _sync; completions are
    // delivered outside the lock and continuations run asynchronously so consumer code
    // never executes on the producing stage thread.
    private readonly object _sync = new();

    private OwnedChunk[] _slots;
    private readonly int _backpressureThreshold;
    private int _head;
    private int _tail;
    private int _count;
    private OwnedChunk _current;
    private ManualResetValueTaskSourceCore<BodyReadResult> _core;
    private bool _readPending;
    private bool _completed;
    private Exception? _fault;

    private readonly int _initialSlotCount;

    public QueuedBodyReader(int capacity)
    {
        _backpressureThreshold = capacity;
        _initialSlotCount = capacity * 2;
        _slots = new OwnedChunk[_initialSlotCount];
        _core.RunContinuationsAsynchronously = true;
    }

    public bool IsBuffered => false;

    public bool IsCompleted
    {
        get
        {
            lock (_sync)
            {
                return _completed && _count == 0 && _current.Rental is null;
            }
        }
    }

    public bool IsFull
    {
        get
        {
            lock (_sync)
            {
                return _count >= _backpressureThreshold;
            }
        }
    }

    public event Action? SlotFreed;

    public bool TryEnqueue(ReadOnlySpan<byte> data)
    {
        var rental = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(rental);
        var chunk = new OwnedChunk(rental, data.Length);

        bool deliverDirectly;
        bool belowThreshold;

        lock (_sync)
        {
            if (_readPending)
            {
                // _readPending is only set while the queue is empty, so direct delivery
                // cannot overtake queued chunks.
                _readPending = false;
                _current = chunk;
                deliverDirectly = true;
            }
            else
            {
                if (_count == _slots.Length)
                {
                    Grow();
                }

                _slots[_tail] = chunk;
                _tail = (_tail + 1) % _slots.Length;
                _count++;
                deliverDirectly = false;
            }

            belowThreshold = _count < _backpressureThreshold;
        }

        if (deliverDirectly)
        {
            _core.SetResult(new BodyReadResult(chunk.Memory, isCompleted: false));
        }

        return belowThreshold;
    }

    public void Complete()
    {
        bool deliver;

        lock (_sync)
        {
            _completed = true;
            deliver = _readPending && _count == 0;
            if (deliver)
            {
                _readPending = false;
            }
        }

        if (deliver)
        {
            _core.SetResult(new BodyReadResult(default, isCompleted: true));
        }
    }

    public void Fault(Exception ex)
    {
        bool deliver;

        lock (_sync)
        {
            _fault = ex;
            deliver = _readPending;
            if (deliver)
            {
                _readPending = false;
            }
        }

        if (deliver)
        {
            _core.SetException(ex);
        }
    }

    public ValueTask<BodyReadResult> ReadAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_count > 0)
            {
                _current = _slots[_head];
                _slots[_head] = default;
                _head = (_head + 1) % _slots.Length;
                _count--;
                return new ValueTask<BodyReadResult>(new BodyReadResult(_current.Memory, isCompleted: false));
            }

            if (_completed)
            {
                return new ValueTask<BodyReadResult>(new BodyReadResult(default, isCompleted: true));
            }

            if (_fault is not null)
            {
                return ValueTask.FromException<BodyReadResult>(_fault);
            }

            if (ct.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<BodyReadResult>(ct);
            }

            // Reset before publishing _readPending: once _readPending is visible, a
            // producer may complete the core at any moment.
            _core.Reset();
            _readPending = true;
        }

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(static (state, token) =>
            {
                var self = (QueuedBodyReader)state!;
                bool deliver;

                lock (self._sync)
                {
                    deliver = self._readPending;
                    if (deliver)
                    {
                        self._readPending = false;
                    }
                }

                if (deliver)
                {
                    self._core.SetException(new OperationCanceledException(token));
                }
            }, this);
        }

        return new ValueTask<BodyReadResult>(this, _core.Version);
    }

    public void AdvanceTo()
    {
        lock (_sync)
        {
            if (_current.Rental is not null)
            {
                ArrayPool<byte>.Shared.Return(_current.Rental);
            }

            _current = default;
        }

        SlotFreed?.Invoke();
    }

    private void Grow()
    {
        var newLength = _slots.Length * 2;
        var newSlots = new OwnedChunk[newLength];

        for (var i = 0; i < _count; i++)
        {
            newSlots[i] = _slots[(_head + i) % _slots.Length];
        }

        _slots = newSlots;
        _head = 0;
        _tail = _count;
    }

    public void Reset()
    {
        bool deliver;

        lock (_sync)
        {
            deliver = _readPending;
            _readPending = false;

            while (_count > 0)
            {
                var chunk = _slots[_head];
                _slots[_head] = default;
                _head = (_head + 1) % _slots.Length;
                _count--;

                if (chunk.Rental is not null)
                {
                    ArrayPool<byte>.Shared.Return(chunk.Rental);
                }
            }

            if (_current.Rental is not null)
            {
                ArrayPool<byte>.Shared.Return(_current.Rental);
            }

            _current = default;
            _head = 0;
            _tail = 0;
            _count = 0;
            _completed = false;
            _fault = null;

            if (!deliver)
            {
                _core = default;
                _core.RunContinuationsAsynchronously = true;
            }

            if (_slots.Length != _initialSlotCount)
            {
                _slots = new OwnedChunk[_initialSlotCount];
            }
        }

        if (deliver)
        {
            // A consumer is still awaiting: complete its pending read instead of
            // resetting the core underneath it.
            _core.SetResult(new BodyReadResult(default, isCompleted: true));
        }
    }

    public Stream AsStream() => new QueuedBodyStream(this);

    public void Dispose() => Reset();

    BodyReadResult IValueTaskSource<BodyReadResult>.GetResult(short token) => _core.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource<BodyReadResult>.GetStatus(short token) => _core.GetStatus(token);

    void IValueTaskSource<BodyReadResult>.OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
