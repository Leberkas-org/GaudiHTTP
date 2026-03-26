using System;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams;
using TurboHttp.Transport;
using InstanceMsg = TurboHttp.Client.ClientStreamInstance;
using OwnerMsg = TurboHttp.Client.ClientStreamOwner;

namespace TurboHttp.Client;

/// <summary>
/// Owns and materializes the Akka.Streams pipeline for a single client lifetime.
/// Reports stream completion or failure back to the parent <see cref="ClientStreamOwnerActor"/>.
/// <para>
/// Lifecycle: receives <see cref="ClientStreamInstance.InitializeStream"/> with externally-owned
/// channels → creates connection pool, materializer, and runs the
/// <c>ChannelSource → Engine → Sink.ForEach</c> graph writing to the provided response writer.
/// On stream sink completion (success or failure), notifies the parent Owner.
/// <see cref="ClientStreamInstance.RequestShutdown"/> signals the graph to drain gracefully.
/// Resources are cleaned up in <see cref="PostStop"/>.
/// </para>
/// </summary>
internal sealed class ClientStreamInstanceActor : UntypedActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    // External channels — owned by TurboClientStreamManager, NOT by this actor.
    // The instance reads from the request reader and writes to the response writer,
    // but never completes them. The manager owns their lifecycle.
    private ChannelReader<HttpRequestMessage>? _requestReader;
    private ChannelWriter<HttpResponseMessage>? _responseWriter;

    // Resources — initialized on InitializeStream, cleaned up in PostStop
    private ConnectionPool? _pool;
    private ActorMaterializer? _materializer;

    // Lifecycle tracking
    private bool _initialized;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case InstanceMsg.InitializeStream init:
                HandleInitializeStream(init);
                break;

            case InstanceMsg.RequestShutdown:
                HandleRequestShutdown();
                break;

            case StreamSinkCompleted completed:
                HandleStreamSinkCompleted(completed);
                break;

            default:
                Unhandled(message);
                break;
        }
    }

    // ── InitializeStream ──────────────────────────────────────────────────

    private void HandleInitializeStream(InstanceMsg.InitializeStream init)
    {
        _log.Debug("Initializing stream pipeline (BaseAddress={0})",
            init.ClientOptions.BaseAddress);

        try
        {
            // Store references to externally-owned channels (not owned by this actor)
            _requestReader = init.RequestReader;
            _responseWriter = init.ResponseWriter;

            // Create connection pool
            _pool = new ConnectionPool(init.ClientOptions.IdleTimeout);

            // Build the engine flow
            var engine = new Engine();
            var engineFlow = engine.CreateFlow(
                _pool,
                init.ClientOptions,
                init.RequestOptionsFactory,
                init.Pipeline);

            // Materialize the graph
            var materializerSettings = ActorMaterializerSettings.Create(Context.System)
                .WithInputBuffer(initialSize: 4, maxSize: 16);
            _materializer = Context.System.Materializer(
                settings: materializerSettings,
                namePrefix: $"stream-instance-{Self.Path.Name}");

            // Use Sink.ForEach to write responses to the externally-owned writer.
            // The sink does NOT own the writer — the manager completes it on shutdown.
            // Sink.ForEach materializes a Task that completes when the stream terminates,
            // which we use for completion monitoring.
            var completionTask = ChannelSource.FromReader(init.RequestReader)
                .Via(engineFlow)
                .RunWith(
                    Sink.ForEach<HttpResponseMessage>(msg => init.ResponseWriter.TryWrite(msg)),
                    _materializer);

            MonitorSinkCompletion(completionTask);

            _initialized = true;
            _log.Debug("Stream pipeline materialized successfully");

            Context.Parent.Tell(new InstanceMsg.StreamInitialized());
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to initialize stream pipeline");
            CleanupResources();
            Context.Parent.Tell(new InstanceMsg.StreamFailed(ex));
        }
    }

    // ── Sink Completion Monitoring ────────────────────────────────────────

    private void MonitorSinkCompletion(Task completionTask)
    {
        // Route the completion notification from the Sink.ForEach task into this
        // actor's mailbox via Self.Tell. This is safe because the actor processes
        // the message through OnReceive on its dispatcher thread.
        var self = Self;

        completionTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                self.Tell(new StreamSinkCompleted(t.Exception?.GetBaseException()));
            }
            else
            {
                self.Tell(new StreamSinkCompleted(null));
            }
        }, TaskScheduler.Default);
    }

    private void HandleStreamSinkCompleted(StreamSinkCompleted completed)
    {
        _log.Debug("Stream sink completed (error: {0})",
            completed.Error?.Message ?? "none");

        if (completed.Error is not null)
        {
            Context.Parent.Tell(new InstanceMsg.StreamFailed(completed.Error));
        }
        else
        {
            // Normal completion — ask Owner if we're allowed to idle
            Context.Parent.Tell(new OwnerMsg.RequestStreamIdle(Self));
        }
    }

    // ── RequestShutdown ──────────────────────────────────────────────────

    private void HandleRequestShutdown()
    {
        _log.Debug("Shutdown requested, stopping actor");

        // Self-stop — resources cleaned up in PostStop.
        // The external channels are NOT completed here; the manager owns their lifecycle.
        Context.Stop(Self);
    }

    // ── PostStop ─────────────────────────────────────────────────────────

    protected override void PostStop()
    {
        _log.Debug("PostStop: cleaning up resources (initialized: {0})", _initialized);
        CleanupResources();
        base.PostStop();
    }

    private void CleanupResources()
    {
        // NOTE: Do NOT complete _requestReader or _responseWriter here.
        // They are externally-owned by TurboClientStreamManager and must remain
        // open for potential retry (new instance reconnecting to same channels).

        // Dispose materializer (stops the Akka stream graph)
        if (_materializer is not null)
        {
            try
            {
                _materializer.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning("Error disposing materializer: {0}", ex.Message);
            }

            _materializer = null;
        }

        // Dispose connection pool
        if (_pool is not null)
        {
            try
            {
                _pool.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning("Error disposing connection pool: {0}", ex.Message);
            }

            _pool = null;
        }
    }

    // ── Internal messages ────────────────────────────────────────────────

    /// <summary>
    /// Internal signal that the stream sink has completed (success or failure).
    /// Routed from the async completion callback into the actor's mailbox.
    /// </summary>
    private sealed record StreamSinkCompleted(Exception? Error);
}
