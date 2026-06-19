using Akka.Actor;

namespace TurboHTTP.Protocol.Body;

internal static class BodyPumpHelper
{
    public const int MaxSyncReadsPerDispatch = 64;

    /// <summary>
    /// Issues one read against <paramref name="slot"/>'s body stream.
    /// <list type="bullet">
    /// <item><description><see cref="ReadOutcome.CompletedSynchronously"/> — data is in <see cref="ReadResult.BytesRead"/>; caller processes inline.</description></item>
    /// <item><description><see cref="ReadOutcome.YieldedForStarvation"/> — starvation guard fired; a <see cref="DrainContinue{TStreamId}"/> was sent to <paramref name="stageActor"/>.</description></item>
    /// <item><description><see cref="ReadOutcome.Dispatched"/> — async read was PipeTo'd; caller waits for <see cref="DrainReadComplete{TStreamId}"/> or <see cref="DrainReadFailed{TStreamId}"/>.</description></item>
    /// </list>
    /// </summary>
    public static ReadResult StartRead<TStreamId>(
        BodyDrainSlot<TStreamId> slot,
        int chunkSize,
        IActorRef stageActor)
    {
        slot.BeginRead();
        var token = slot.LinkedCts?.Token ?? CancellationToken.None;
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..chunkSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            slot.CompleteSyncRead();

            if (slot.IncrementSyncReads(MaxSyncReadsPerDispatch))
            {
                slot.ResetSyncReads();
                stageActor.Tell(new DrainContinue<TStreamId>(slot.StreamId), ActorRefs.NoSender);
                return new ReadResult(ReadOutcome.YieldedForStarvation, 0);
            }

            return new ReadResult(ReadOutcome.CompletedSynchronously, vt.Result);
        }

        var streamId = slot.StreamId;
        vt.PipeTo(
            stageActor,
            success: n => new DrainReadComplete<TStreamId>(streamId, n),
            failure: ex => new DrainReadFailed<TStreamId>(streamId, ex));
        return new ReadResult(ReadOutcome.Dispatched, 0);
    }

    internal readonly record struct ReadResult(ReadOutcome Outcome, int BytesRead);

    internal enum ReadOutcome
    {
        CompletedSynchronously,
        Dispatched,
        YieldedForStarvation
    }
}
