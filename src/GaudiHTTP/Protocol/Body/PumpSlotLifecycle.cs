using Akka.Actor;

namespace GaudiHTTP.Protocol.Body;

// Static composition helpers shared by the PumpSlot-based pumps (FlowControlledBodyPump,
// MultiplexedBodyPump). SerialBodyPump does not use PumpSlot<T> (it tracks a single stream via
// plain fields), so it is not a candidate for these helpers. Deliberately NOT a base class — a
// BodyPumpBase inheritance unification was tried and reverted; use composition only.
internal static class PumpSlotLifecycle
{
    // Removes the slot from the active-slot map and releases its resources. Shared by every
    // orphan/cancel/teardown path that does not need to notify the drain target first.
    public static void ReleaseSlot<TStreamId>(
        Dictionary<TStreamId, PumpSlot<TStreamId>> activeSlots,
        TStreamId streamId,
        PumpSlot<TStreamId> slot) where TStreamId : notnull
    {
        activeSlots.Remove(streamId);
        slot.DisposeResources();
        slot.Dispose();
    }

    // Starts the read and forces the completion to always flow through the actor mailbox, even
    // when the ValueTask is already completed. Delivering the already-known result synchronously
    // (instead of dispatching it) would let a re-entrant schedule touch slot.Buffer before the
    // "in-flight" completion is processed. The slot stays IsReadInFlight (from BeginRead) and
    // counted in asyncInFlight across the mailbox hop so the completion handler's
    // CompleteRead/asyncInFlight-- balance always holds.
    public static void StartRead<TStreamId>(
        PumpSlot<TStreamId> slot,
        int readSize,
        CancellationToken token,
        IActorRef stageActor,
        ref int asyncInFlight)
    {
        slot.BeginRead();
        var vt = slot.BodyStream!.ReadAsync(slot.Buffer!.Memory[..readSize], token);

        if (vt.IsCompletedSuccessfully)
        {
            asyncInFlight++;
            stageActor.Tell(slot.CachedSuccessTransform!(vt.Result), ActorRefs.NoSender);
            return;
        }

        asyncInFlight++;
        vt.PipeTo(
            stageActor,
            success: slot.CachedSuccessTransform,
            failure: slot.CachedFailureTransform);
    }
}
