namespace TurboHTTP.Streams.Stages.Server;

internal sealed class FairShareDispatcher(int totalLimit, int minGuarantee)
{
    private readonly int _configuredGuarantee = minGuarantee;
    private readonly Lock _lock = new();
    private readonly Dictionary<int, int> _connectionInFlight = [];
    private readonly Dictionary<int, Action?> _slotCallbacks = [];
    private int _totalInFlight;
    private int _effectiveGuarantee = minGuarantee;

    public int EffectiveGuarantee
    {
        get
        {
            lock (_lock)
            {
                return _effectiveGuarantee;
            }
        }
    }

    public void RegisterConnection(int connectionId)
    {
        lock (_lock)
        {
            _connectionInFlight[connectionId] = 0;
            _slotCallbacks[connectionId] = null;
            RecalculateGuarantee();
        }
    }

    public void UnregisterConnection(int connectionId)
    {
        lock (_lock)
        {
            if (_connectionInFlight.TryGetValue(connectionId, out var inFlight))
            {
                _totalInFlight -= inFlight;
            }

            _connectionInFlight.Remove(connectionId);
            _slotCallbacks.Remove(connectionId);
            RecalculateGuarantee();
        }
    }

    public bool TryAcquire(int connectionId)
    {
        lock (_lock)
        {
            if (totalLimit > 0 && _totalInFlight >= totalLimit)
            {
                return false;
            }

            if (!_connectionInFlight.TryGetValue(connectionId, out var current))
            {
                return false;
            }

            if (current < _effectiveGuarantee)
            {
                _connectionInFlight[connectionId] = current + 1;
                _totalInFlight++;
                return true;
            }

            var sharedPool = ComputeSharedPool();
            var sharedUsed = ComputeSharedUsed();
            if (sharedUsed < sharedPool)
            {
                _connectionInFlight[connectionId] = current + 1;
                _totalInFlight++;
                return true;
            }

            return false;
        }
    }

    public void Release(int connectionId)
    {
        Action? callback = null;
        lock (_lock)
        {
            if (!_connectionInFlight.TryGetValue(connectionId, out var current) || current <= 0)
            {
                return;
            }

            _connectionInFlight[connectionId] = current - 1;
            _totalInFlight--;

            foreach (var (connId, cb) in _slotCallbacks)
            {
                if (cb is not null)
                {
                    callback = cb;
                    _slotCallbacks[connId] = null;
                    break;
                }
            }
        }

        callback?.Invoke();
    }

    public void RegisterSlotAvailableCallback(int connectionId, Action callback)
    {
        lock (_lock)
        {
            if (_slotCallbacks.ContainsKey(connectionId))
            {
                _slotCallbacks[connectionId] = callback;
            }
        }
    }

    public int GetConnectionInFlight(int connectionId)
    {
        lock (_lock)
        {
            return _connectionInFlight.GetValueOrDefault(connectionId, 0);
        }
    }

    private int ComputeSharedPool()
    {
        if (totalLimit == 0)
        {
            return int.MaxValue;
        }

        var reserved = _connectionInFlight.Count * _effectiveGuarantee;
        return Math.Max(0, totalLimit - reserved);
    }

    private int ComputeSharedUsed()
    {
        var sharedUsed = 0;
        foreach (var (_, inFlight) in _connectionInFlight)
        {
            if (inFlight > _effectiveGuarantee)
            {
                sharedUsed += inFlight - _effectiveGuarantee;
            }
        }

        return sharedUsed;
    }

    private void RecalculateGuarantee()
    {
        var count = _connectionInFlight.Count;
        if (count == 0 || totalLimit == 0)
        {
            _effectiveGuarantee = _configuredGuarantee;
            return;
        }

        if (count * _configuredGuarantee > totalLimit)
        {
            _effectiveGuarantee = totalLimit / count;
        }
        else
        {
            _effectiveGuarantee = _configuredGuarantee;
        }
    }
}