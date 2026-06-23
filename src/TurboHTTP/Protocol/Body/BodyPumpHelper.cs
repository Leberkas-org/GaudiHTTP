using Akka.Actor;

namespace TurboHTTP.Protocol.Body;

internal static class BodyPumpHelper
{
    public const int MaxSyncReadsPerDispatch = 64;

    /// <summary>
    /// Issues one read against <paramref name="slot"/>'s body stream.
    /// <list type="bullet">
    /// <item><description><see cref="ReadOutcome.CompletedSynchronously"/> — data is in <see cref="ReadResult.BytesRead"/>; caller processes inline.</description></item>
    /// <item><description><see cref="ReadOutcome.Dispatched"/> — async read was PipeTo'd; caller waits for <see cref="DrainReadComplete{TStreamId}"/> or <see cref="DrainReadFailed{TStreamId}"/>.</description></item>
    /// </list>
    /// Caller is responsible for starvation guard (checking <see cref="BodyDrainSlot{TStreamId}.ConsecutiveSyncReads"/>
    /// against <see cref="MaxSyncReadsPerDispatch"/> before calling this method).
    /// </summary>
    public static ReadResult StartRead<TStreamId>(
        BodyDrainSlot<TStreamId> slot,
        int chunkSize,
        IActorRef stageActor)
    {
        slot.BeginRead();
        var token = slot.LinkedCts?.Token ?? slot.RequestCt;
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..chunkSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            slot.CompleteSyncRead();
            slot.IncrementSyncReads(MaxSyncReadsPerDispatch);
            return new ReadResult(ReadOutcome.CompletedSynchronously, vt.Result);
        }

        vt.PipeTo(
            stageActor,
            success: slot.CachedSuccessTransform,
            failure: slot.CachedFailureTransform);
        return new ReadResult(ReadOutcome.Dispatched, 0);
    }

    internal readonly record struct ReadResult(ReadOutcome Outcome, int BytesRead);

    internal enum ReadOutcome
    {
        CompletedSynchronously,
        Dispatched
    }
}
