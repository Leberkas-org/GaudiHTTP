using System;
using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Streams;

namespace TurboHttp.Client;

// ──────────────────────────────────────────────────────────────────────────────
// IPendingWorkTracker — shared counter between feature BidiStages and the Owner
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tracks the number of in-flight re-injections from feature BidiStages
/// (Retry, Cache, Compression). The Owner actor checks this before allowing
/// the stream instance to complete, preventing the HTTP/1.0 deadlock where
/// <c>GroupByHostKeyStage</c> completes a substream while a feature stage
/// still needs to push a follow-up request.
/// <para>
/// Thread safety: implementations must be safe for concurrent calls from
/// multiple stage logic callbacks running on the Akka dispatcher.
/// </para>
/// </summary>
public interface IPendingWorkTracker
{
    /// <summary>
    /// Increments the pending work counter. Called by a feature BidiStage
    /// <b>before</b> it pushes a re-injected request upstream (e.g., retry
    /// after 503, cache revalidation, decompression re-request).
    /// </summary>
    void IncrementPending();

    /// <summary>
    /// Decrements the pending work counter. Called by a feature BidiStage
    /// <b>after</b> the re-injected request has completed its round-trip
    /// and the final response has been pushed downstream.
    /// </summary>
    void DecrementPending();

    /// <summary>
    /// Returns <see langword="true"/> when one or more feature stages have
    /// pending re-injections in flight. The Owner checks this before allowing
    /// <c>GroupByHostKeyStage</c> to complete a substream.
    /// </summary>
    bool IsPending { get; }
}

// ──────────────────────────────────────────────────────────────────────────────
// ClientStreamOwner.Message — messages received by the Owner actor
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Protocol messages for <c>ClientStreamOwner</c>, the actor that manages
/// stream lifecycle, tracks pending work, and supervises the stream instance.
/// </summary>
public static class ClientStreamOwner
{
    /// <summary>Base type for all messages handled by <c>ClientStreamOwner</c>.</summary>
    public abstract record Message;

    /// <summary>
    /// Requests the Owner to create and supervise a new <c>ClientStreamInstance</c>.
    /// <para>
    /// <b>Trigger:</b> Sent by <c>TurboClientStreamManager</c> during client initialization.<br/>
    /// <b>Expected response:</b> Owner spawns a child <c>ClientStreamInstance</c> actor, sends it
    /// <see cref="ClientStreamInstance.InitializeStream"/>, and replies with
    /// <see cref="StreamInstanceCreated"/> on success or <see cref="StreamInstanceFailed"/> on failure.<br/>
    /// <b>Cleanup:</b> None — the Owner owns the child actor's lifecycle.
    /// </para>
    /// </summary>
    internal sealed record CreateStreamInstance(
        TurboClientOptions ClientOptions,
        Func<TurboRequestOptions> RequestOptionsFactory,
        PipelineDescriptor Pipeline,
        ChannelReader<HttpRequestMessage> RequestReader,
        ChannelWriter<HttpResponseMessage> ResponseWriter) : Message;

    /// <summary>
    /// Confirms that the stream instance was successfully created and initialized.
    /// <para>
    /// <b>Trigger:</b> Sent by the Owner to itself (or to the original requester) after the
    /// child <c>ClientStreamInstance</c> sends <see cref="ClientStreamInstance.StreamInitialized"/>.<br/>
    /// <b>Expected response:</b> The requester may begin sending requests through the stream channels.<br/>
    /// <b>Cleanup:</b> None.
    /// </para>
    /// </summary>
    public sealed record StreamInstanceCreated(IActorRef InstanceRef) : Message;

    /// <summary>
    /// Reports that stream instance creation or initialization failed.
    /// <para>
    /// <b>Trigger:</b> Sent by the Owner to itself (or to the original requester) when the child
    /// actor fails during spawn or when <see cref="ClientStreamInstance.StreamFailed"/> is received.<br/>
    /// <b>Expected response:</b> The Owner applies retry logic (exponential backoff: 100ms, 500ms, 2s,
    /// max 3 attempts). If retries are exhausted, the failure propagates to the requester.<br/>
    /// <b>Cleanup:</b> The failed child actor is stopped by the supervision strategy.
    /// </para>
    /// </summary>
    public sealed record StreamInstanceFailed(Exception Reason, int AttemptNumber) : Message;

    /// <summary>
    /// Notifies the Owner that the pending work count has changed.
    /// <para>
    /// <b>Trigger:</b> Sent by the <see cref="IPendingWorkTracker"/> implementation when a feature
    /// BidiStage calls <see cref="IPendingWorkTracker.IncrementPending"/> or
    /// <see cref="IPendingWorkTracker.DecrementPending"/>.<br/>
    /// <b>Expected response:</b> The Owner updates its internal pending count. If the count reaches
    /// zero and a <see cref="RequestStreamIdle"/> is pending, the Owner signals the instance to complete.<br/>
    /// <b>Cleanup:</b> None.
    /// </para>
    /// </summary>
    public sealed record PendingWorkSignal(int Delta) : Message;

    /// <summary>
    /// Requests permission to idle/complete the stream instance.
    /// <para>
    /// <b>Trigger:</b> Sent by <c>ClientStreamInstance</c> (or <c>GroupByHostKeyStage</c>) when
    /// upstream signals completion and the instance wants to shut down.<br/>
    /// <b>Expected response:</b> The Owner checks <see cref="IPendingWorkTracker.IsPending"/>.
    /// If <see langword="false"/>, the Owner allows completion. If <see langword="true"/>, the Owner
    /// defers completion until pending work drains (re-checked on each <see cref="PendingWorkSignal"/>).<br/>
    /// <b>Cleanup:</b> None — completion proceeds through normal Akka stream lifecycle.
    /// </para>
    /// </summary>
    public sealed record RequestStreamIdle(IActorRef RequestedBy) : Message;

    /// <summary>
    /// Requests graceful shutdown of the Owner and all child actors.
    /// <para>
    /// <b>Trigger:</b> Sent by <c>TurboClientStreamManager.Dispose()</c> or externally.<br/>
    /// <b>Expected response:</b> The Owner completes the request channel, waits up to 5 seconds for
    /// the stream instance to drain pending work, then terminates the child actor and itself.<br/>
    /// <b>Cleanup:</b> Owner is responsible for stopping the child and self-terminating.
    /// </para>
    /// </summary>
    public sealed record Shutdown : Message;
}

// ──────────────────────────────────────────────────────────────────────────────
// ClientStreamInstance.Message — messages received by the Instance actor
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Protocol messages for <c>ClientStreamInstance</c>, the actor that owns and
/// materializes the Akka.Streams pipeline for a single client lifetime.
/// </summary>
public static class ClientStreamInstance
{
    /// <summary>Base type for all messages handled by <c>ClientStreamInstance</c>.</summary>
    public abstract record Message;

    /// <summary>
    /// Instructs the Instance to materialize the Akka.Streams pipeline.
    /// <para>
    /// <b>Trigger:</b> Sent by the Owner immediately after spawning the child actor.<br/>
    /// <b>Expected response:</b> The Instance materializes <c>ChannelSource → Engine → ChannelSink</c>,
    /// then replies with <see cref="StreamInitialized"/>. On failure, replies with
    /// <see cref="StreamFailed"/>.<br/>
    /// <b>Cleanup:</b> If materialization fails, the Instance should not hold any resources
    /// (materializer, channels, pool) — they are cleaned up before sending <see cref="StreamFailed"/>.
    /// </para>
    /// </summary>
    internal sealed record InitializeStream(
        TurboClientOptions ClientOptions,
        Func<TurboRequestOptions> RequestOptionsFactory,
        PipelineDescriptor Pipeline,
        ChannelReader<HttpRequestMessage> RequestReader,
        ChannelWriter<HttpResponseMessage> ResponseWriter) : Message;

    /// <summary>
    /// Confirms that the stream pipeline has been successfully materialized.
    /// <para>
    /// <b>Trigger:</b> Sent by the Instance to its parent Owner after successful materialization.<br/>
    /// <b>Expected response:</b> The Owner transitions to "running" state and replies
    /// <see cref="ClientStreamOwner.StreamInstanceCreated"/> to the original requester.<br/>
    /// <b>Cleanup:</b> None.
    /// </para>
    /// </summary>
    public sealed record StreamInitialized : Message;

    /// <summary>
    /// Reports that stream materialization or runtime execution failed.
    /// <para>
    /// <b>Trigger:</b> Sent by the Instance to its parent Owner when materialization throws,
    /// or when the stream sink completes with an error.<br/>
    /// <b>Expected response:</b> The Owner receives this and applies retry/supervision logic
    /// via <see cref="ClientStreamOwner.StreamInstanceFailed"/>.<br/>
    /// <b>Cleanup:</b> The Instance disposes its materializer, closes channels, and disposes
    /// the connection pool in <c>PostStop()</c>.
    /// </para>
    /// </summary>
    public sealed record StreamFailed(Exception Reason) : Message;

    /// <summary>
    /// Notifies the Owner that pending work count has changed from this instance's perspective.
    /// <para>
    /// <b>Trigger:</b> Forwarded by the Instance when it observes a feature BidiStage
    /// increment/decrement via the <see cref="IPendingWorkTracker"/>.<br/>
    /// <b>Expected response:</b> The Owner updates its pending count tracking.<br/>
    /// <b>Cleanup:</b> None.
    /// </para>
    /// </summary>
    public sealed record PendingWorkChanged(int NewCount) : Message;

    /// <summary>
    /// Requests the Instance to shut down gracefully.
    /// <para>
    /// <b>Trigger:</b> Sent by the Owner when it decides the Instance should complete
    /// (either because <see cref="ClientStreamOwner.Shutdown"/> was received and pending
    /// work is zero, or because the shutdown timeout expired).<br/>
    /// <b>Expected response:</b> The Instance completes its request channel, allowing the
    /// pipeline to drain. After the sink completes, the actor stops via <c>PostStop()</c>.<br/>
    /// <b>Cleanup:</b> The Instance is responsible for disposing materializer, channels,
    /// and pool in <c>PostStop()</c>.
    /// </para>
    /// </summary>
    public sealed record RequestShutdown : Message;
}

// ──────────────────────────────────────────────────────────────────────────────
// Message Flow Diagrams
// ──────────────────────────────────────────────────────────────────────────────
//
// HAPPY PATH: Create → Initialize → Run → Idle → Shutdown
// ─────────────────────────────────────────────────────────
//
//   StreamManager              Owner                     Instance
//       │                        │                          │
//       │──CreateStreamInstance──▶│                          │
//       │                        │──spawn──▶                │
//       │                        │──InitializeStream───────▶│
//       │                        │                          │──materialize pipeline
//       │                        │◀──StreamInitialized──────│
//       │◀─StreamInstanceCreated─│                          │
//       │                        │                          │
//       │    ... requests flow through channels ...         │
//       │                        │                          │
//       │                        │  (feature stage re-injects request)
//       │                        │◀─PendingWorkSignal(+1)──│  (via tracker)
//       │                        │  ... re-injection completes ...
//       │                        │◀─PendingWorkSignal(-1)──│  (via tracker)
//       │                        │                          │
//       │                        │◀─RequestStreamIdle──────│  (upstream done)
//       │                        │  check: IsPending == false
//       │                        │──RequestShutdown────────▶│
//       │                        │                          │──complete channels
//       │                        │                          │──PostStop() cleanup
//       │                        │                          ╳  (actor stopped)
//       │◀───(actor terminated)──│
//       │                        ╳
//
//
// ERROR PATH: Instance Crash → Retry with Backoff
// ────────────────────────────────────────────────
//
//   StreamManager              Owner                     Instance
//       │                        │                          │
//       │──CreateStreamInstance──▶│                          │
//       │                        │──spawn + init───────────▶│
//       │                        │                          │──materialize fails
//       │                        │◀──StreamFailed(ex)──────│
//       │                        │                          ╳  (actor stopped)
//       │                        │
//       │                        │  (retry attempt 1, backoff 100ms)
//       │                        │──spawn + init───────────▶│  (new instance)
//       │                        │                          │──materialize fails
//       │                        │◀──StreamFailed(ex)──────│
//       │                        │                          ╳
//       │                        │
//       │                        │  (retry attempt 2, backoff 500ms)
//       │                        │──spawn + init───────────▶│  (new instance)
//       │                        │◀──StreamInitialized──────│  (success!)
//       │◀─StreamInstanceCreated─│                          │
//       │                        │                          │
//
//
// ERROR PATH: Retries Exhausted
// ─────────────────────────────
//
//   StreamManager              Owner                     Instance
//       │                        │                          │
//       │──CreateStreamInstance──▶│                          │
//       │                        │  ... 3 failed attempts (100ms, 500ms, 2s) ...
//       │                        │                          ╳
//       │◀─StreamInstanceFailed──│  (retries exhausted)
//       │  (propagate error)     │
//       │                        ╳
//
//
// SHUTDOWN PATH: Graceful with Pending Work
// ──────────────────────────────────────────
//
//   StreamManager              Owner                     Instance
//       │                        │                          │
//       │──Shutdown─────────────▶│                          │
//       │                        │  check: IsPending == true
//       │                        │  (wait for pending work, up to 5s timeout)
//       │                        │◀─PendingWorkSignal(-1)──│  (last pending completes)
//       │                        │  check: IsPending == false
//       │                        │──RequestShutdown────────▶│
//       │                        │                          │──drain + PostStop()
//       │                        │                          ╳
//       │◀───(actor terminated)──│
//       │                        ╳
//
//
// SHUTDOWN PATH: Timeout Exceeded
// ───────────────────────────────
//
//   StreamManager              Owner                     Instance
//       │                        │                          │
//       │──Shutdown─────────────▶│                          │
//       │                        │  check: IsPending == true
//       │                        │  (5 second timeout expires)
//       │                        │──RequestShutdown────────▶│  (force)
//       │                        │                          │──drain + PostStop()
//       │                        │                          ╳
//       │◀───(actor terminated)──│
//       │                        ╳
