using Akka.Actor;

namespace TurboHTTP.Protocol.Body;

internal sealed class SerialBodyPump
{
    private readonly IBodyDrainTarget<int> _target;
    private readonly CancellationTokenSource _connectionCts;
    private readonly int _chunkSize;
    private readonly int _maxCapacity;

    private readonly BodyDrainSlot<int> _slot = new();

    private int _availableCapacity;

    public SerialBodyPump(
        IBodyDrainTarget<int> target,
        CancellationTokenSource connectionCts,
        int chunkSize,
        int maxCapacity)
    {
        _target = target;
        _connectionCts = connectionCts;
        _chunkSize = chunkSize;
        _maxCapacity = maxCapacity;
    }

    public void Register(Stream bodyStream, long? contentLength, CancellationToken requestCt)
    {
        var linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);
        _slot.Initialize(0, bodyStream, contentLength, requestCt, linkedCts);
        _slot.EnsureBuffer(_chunkSize);
        _availableCapacity = _maxCapacity;
        TryStartRead();
    }

    public void OnCapacityAvailable()
    {
        if (_availableCapacity < _maxCapacity)
        {
            _availableCapacity++;
        }

        TryStartRead();
    }

    public void ResetSyncReadCounter()
    {
        _slot.ResetSyncReads();
    }

    public void HandleReadComplete(int bytesRead)
    {
        _slot.CompleteAsyncRead();
        ProcessReadResult(bytesRead);
    }

    public void HandleReadFailed(Exception reason)
    {
        _slot.CompleteAsyncRead();
        _target.OnDrainFailed(0, reason);
        CompleteDrain();
    }

    public void HandleDrainContinue()
    {
        _slot.CompleteSyncRead();
        TryStartRead();
    }

    public void Cancel()
    {
        _slot.LinkedCts?.Cancel();
        _slot.DisposeResources();
        _slot.Reset();
        _availableCapacity = 0;
    }

    public void Cleanup()
    {
        _slot.DisposeResources();
        _slot.Reset();
        _availableCapacity = 0;
    }

    private void TryStartRead()
    {
        if (_availableCapacity <= 0 || _slot.IsReadInFlight || _slot.BodyStream is null)
        {
            return;
        }

        _availableCapacity--;
        var result = BodyPumpHelper.StartRead(_slot, _chunkSize, _target.PipeToTarget);

        switch (result.Outcome)
        {
            case BodyPumpHelper.ReadOutcome.CompletedSynchronously:
                ProcessReadResult(result.BytesRead);
                break;

            case BodyPumpHelper.ReadOutcome.Dispatched:
                // Async — HandleReadComplete/HandleReadFailed will be called.
                break;
        }
    }

    private void ProcessReadResult(int bytesRead)
    {
        if (bytesRead == 0)
        {
            _target.EmitDataFrames(0, default, endStream: true);
            CompleteDrain();
            return;
        }

        _target.EmitDataFrames(0, _slot.Buffer!.Memory[..bytesRead], endStream: false);
        TryStartRead();
    }

    private void CompleteDrain()
    {
        var wasActive = _slot.BodyStream is not null;
        _slot.DisposeResources();
        _slot.Reset();
        _availableCapacity = 0;

        if (wasActive)
        {
            _target.OnDrainComplete(0);
        }
    }
}
