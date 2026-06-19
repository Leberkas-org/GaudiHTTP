using System.Buffers;
using Akka.Actor;

namespace TurboHTTP.Protocol.Body;

internal sealed class SerialBodyPump
{
    private const int MaxSyncReadsPerDispatch = 64;

    private readonly IBodyDrainTarget _target;
    private readonly CancellationTokenSource _connectionCts;
    private readonly int _chunkSize;
    private readonly int _maxCapacity;

    private Stream? _activeStream;
    private IMemoryOwner<byte>? _buffer;
    private CancellationTokenSource? _linkedCts;
    private bool _isReadInFlight;
    private int _availableCapacity;
    private int _consecutiveSyncReads;

    public SerialBodyPump(
        IBodyDrainTarget target,
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
        _activeStream = bodyStream;
        _buffer ??= MemoryPool<byte>.Shared.Rent(Math.Max(_chunkSize, 256));
        _linkedCts = requestCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token, requestCt)
            : null;
        _availableCapacity = _maxCapacity;
        _consecutiveSyncReads = 0;
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

    public void HandleReadComplete(int bytesRead)
    {
        _isReadInFlight = false;
        _consecutiveSyncReads = 0;
        ProcessReadResult(bytesRead);
    }

    public void HandleReadFailed(Exception reason)
    {
        _isReadInFlight = false;
        _consecutiveSyncReads = 0;
        _target.OnDrainFailed(0, reason);
        CompleteDrain();
    }

    public void HandleDrainContinue()
    {
        _isReadInFlight = false;
        TryStartRead();
    }

    public void Cancel()
    {
        _linkedCts?.Cancel();
        _buffer?.Dispose();
        _buffer = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableCapacity = 0;
        _isReadInFlight = false;
    }

    public void Cleanup()
    {
        _buffer?.Dispose();
        _buffer = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableCapacity = 0;
        _isReadInFlight = false;
    }

    private void TryStartRead()
    {
        if (_availableCapacity <= 0 || _isReadInFlight || _activeStream is null)
        {
            return;
        }

        // Starvation guard: yield after MaxSyncReadsPerDispatch consecutive sync reads
        // so the actor thread can process other messages between bursts.
        if (_consecutiveSyncReads >= MaxSyncReadsPerDispatch)
        {
            _consecutiveSyncReads = 0;
            // Use _isReadInFlight as a yield-in-progress marker so that any re-entrant
            // call from ProcessReadResult cannot start another read while we wait for
            // HandleDrainContinue to resume us.
            _isReadInFlight = true;
            _target.StageActor.Tell(new DrainContinue(0), ActorRefs.NoSender);
            return;
        }

        _availableCapacity--;
        var token = _linkedCts?.Token ?? _connectionCts.Token;
        _isReadInFlight = true;
        var vt = _activeStream.ReadAsync(_buffer!.Memory[.._chunkSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            _isReadInFlight = false;
            _consecutiveSyncReads++;
            ProcessReadResult(vt.Result);
            return;
        }

        _consecutiveSyncReads = 0;
        vt.PipeTo(
            _target.StageActor,
            success: bytesRead => new DrainReadComplete(0, bytesRead),
            failure: ex => new DrainReadFailed(0, ex));
    }

    private void ProcessReadResult(int bytesRead)
    {
        if (bytesRead == 0)
        {
            _target.EmitDataFrames(0, default, endStream: true);
            CompleteDrain();
            return;
        }

        _target.EmitDataFrames(0, _buffer!.Memory[..bytesRead], endStream: false);
        TryStartRead();
    }

    private void CompleteDrain()
    {
        var wasActive = _activeStream is not null;
        _buffer?.Dispose();
        _buffer = null;
        _linkedCts?.Dispose();
        _linkedCts = null;
        _activeStream = null;
        _availableCapacity = 0;

        if (wasActive)
        {
            _target.OnDrainComplete(0);
        }
    }
}
