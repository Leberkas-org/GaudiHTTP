using Akka.Actor;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class FairShareCoordinator : ReceiveActor
{
    public sealed record Register(int ConnectionId);
    public sealed record Unregister(int ConnectionId);
    public sealed record Acquire(int ConnectionId, IActorRef ReplyTo);
    public sealed record Granted;
    public sealed record Release(int ConnectionId);
    public sealed record GetEffectiveGuarantee(IActorRef ReplyTo);
    public sealed record EffectiveGuaranteeReply(int Value);

    private readonly int _totalLimit;
    private readonly int _configuredGuarantee;
    private readonly Dictionary<int, int> _connectionInFlight = [];
    private readonly Queue<Acquire> _pendingAcquires = new();
    private int _totalInFlight;
    private int _effectiveGuarantee;

    public static Props Props(int totalLimit, int minGuarantee)
        => Akka.Actor.Props.Create(() => new FairShareCoordinator(totalLimit, minGuarantee));

    public FairShareCoordinator(int totalLimit, int minGuarantee)
    {
        _totalLimit = totalLimit;
        _configuredGuarantee = minGuarantee;
        _effectiveGuarantee = minGuarantee;

        Receive<Register>(OnRegister);
        Receive<Unregister>(OnUnregister);
        Receive<Acquire>(OnAcquire);
        Receive<Release>(OnRelease);
        Receive<GetEffectiveGuarantee>(msg => msg.ReplyTo.Tell(new EffectiveGuaranteeReply(_effectiveGuarantee)));
    }

    private void OnRegister(Register msg)
    {
        _connectionInFlight[msg.ConnectionId] = 0;
        RecalculateGuarantee();
    }

    private void OnUnregister(Unregister msg)
    {
        if (_connectionInFlight.TryGetValue(msg.ConnectionId, out var inFlight))
        {
            _totalInFlight -= inFlight;
        }

        _connectionInFlight.Remove(msg.ConnectionId);
        RecalculateGuarantee();
        TryGrantPending();
    }

    private void OnAcquire(Acquire msg)
    {
        if (TryAcquireSlot(msg.ConnectionId))
        {
            msg.ReplyTo.Tell(new Granted());
        }
        else
        {
            _pendingAcquires.Enqueue(msg);
        }
    }

    private void OnRelease(Release msg)
    {
        if (!_connectionInFlight.TryGetValue(msg.ConnectionId, out var current) || current <= 0)
        {
            return;
        }

        _connectionInFlight[msg.ConnectionId] = current - 1;
        _totalInFlight--;
        TryGrantPending();
    }

    private bool TryAcquireSlot(int connectionId)
    {
        if (_totalLimit > 0 && _totalInFlight >= _totalLimit)
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

    private void TryGrantPending()
    {
        var retryQueue = new Queue<Acquire>();
        while (_pendingAcquires.Count > 0)
        {
            var pending = _pendingAcquires.Dequeue();
            if (!_connectionInFlight.ContainsKey(pending.ConnectionId))
            {
                continue;
            }

            if (TryAcquireSlot(pending.ConnectionId))
            {
                pending.ReplyTo.Tell(new Granted());
            }
            else
            {
                retryQueue.Enqueue(pending);
            }
        }

        while (retryQueue.Count > 0)
        {
            _pendingAcquires.Enqueue(retryQueue.Dequeue());
        }
    }

    private int ComputeSharedPool()
    {
        if (_totalLimit == 0)
        {
            return int.MaxValue;
        }

        var reserved = _connectionInFlight.Count * _effectiveGuarantee;
        return Math.Max(0, _totalLimit - reserved);
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
        if (count == 0 || _totalLimit == 0)
        {
            _effectiveGuarantee = _configuredGuarantee;
            return;
        }

        _effectiveGuarantee = count * _configuredGuarantee > _totalLimit
            ? _totalLimit / count
            : _configuredGuarantee;
    }
}
