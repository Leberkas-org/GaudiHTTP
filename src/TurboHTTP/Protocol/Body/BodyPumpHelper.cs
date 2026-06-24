using Akka.Actor;

namespace TurboHTTP.Protocol.Body;

internal static class BodyPumpHelper
{
    /// <summary>
    /// Issues one read against <paramref name="slot"/>'s body stream.
    /// <list type="bullet">
    /// <item><description><see cref="ReadOutcome.CompletedSynchronously"/> — data is in <see cref="ReadResult.BytesRead"/>; caller processes inline.</description></item>
    /// <item><description><see cref="ReadOutcome.Dispatched"/> — async read was PipeTo'd; caller waits for <see cref="DrainReadComplete{TStreamId}"/> or <see cref="DrainReadFailed{TStreamId}"/>.</description></item>
    /// </list>
    /// </summary>
    public static ReadResult StartRead<TStreamId>(
        BodyDrainSlot<TStreamId> slot,
        int chunkSize,
        IActorRef pipeToTarget)
    {
        slot.BeginRead();
        var token = slot.LinkedCts?.Token ?? slot.RequestCt;
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..chunkSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            slot.CompleteSyncRead();
            return new ReadResult(ReadOutcome.CompletedSynchronously, vt.Result);
        }

        vt.PipeTo(
            pipeToTarget,
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
