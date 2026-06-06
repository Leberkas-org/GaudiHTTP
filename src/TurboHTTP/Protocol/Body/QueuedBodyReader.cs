using System.Buffers;
using System.Threading.Tasks.Sources;

namespace TurboHTTP.Protocol.Body;

internal sealed class QueuedBodyReader : IStreamingBodyReader, IValueTaskSource<BodyReadResult>
{
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
    }

    public bool IsBuffered => false;
    public bool IsCompleted => _completed && _count == 0 && _current.Rental is null;
    public bool IsFull => _count >= _backpressureThreshold;
    public event Action? SlotFreed;

    public bool TryEnqueue(ReadOnlySpan<byte> data)
    {
        var rental = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(rental);
        var chunk = new OwnedChunk(rental, data.Length);

        if (_readPending)
        {
            _readPending = false;
            _current = chunk;
            _core.SetResult(new BodyReadResult(chunk.Memory, isCompleted: false));
            return _count < _backpressureThreshold;
        }

        if (_count == _slots.Length)
        {
            Grow();
        }

        _slots[_tail] = chunk;
        _tail = (_tail + 1) % _slots.Length;
        _count++;
        return _count < _backpressureThreshold;
    }

    public void Complete()
    {
        _completed = true;

        if (_readPending && _count == 0)
        {
            _readPending = false;
            _core.SetResult(new BodyReadResult(default, isCompleted: true));
        }
    }

    public void Fault(Exception ex)
    {
        _fault = ex;

        if (_readPending)
        {
            _readPending = false;
            _core.SetException(ex);
        }
    }

    public ValueTask<BodyReadResult> ReadAsync(CancellationToken ct = default)
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

        _readPending = true;
        _core.Reset();
        return new ValueTask<BodyReadResult>(this, _core.Version);
    }

    public void AdvanceTo()
    {
        if (_current.Rental is not null)
        {
            ArrayPool<byte>.Shared.Return(_current.Rental);
        }

        _current = default;
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
        _readPending = false;
        _completed = false;
        _fault = null;
        _core = default;

        if (_slots.Length != _initialSlotCount)
        {
            _slots = new OwnedChunk[_initialSlotCount];
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
