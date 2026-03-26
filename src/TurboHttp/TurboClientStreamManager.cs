using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using TurboHttp.Client;
using TurboHttp.Streams;

namespace TurboHttp;

/// <summary>
/// Internal wrapper around <see cref="ClientStreamOwnerActor"/> that maintains backward
/// compatibility with the existing <see cref="TurboHttpClient"/> API.
/// <para>
/// Owns the stable channel endpoints (<see cref="Requests"/> / <see cref="Responses"/>)
/// and delegates stream lifecycle management to the actor hierarchy.
/// The Owner actor supervises a <see cref="ClientStreamInstanceActor"/> that materializes
/// the Akka.Streams pipeline using these externally-owned channels.
/// </para>
/// <para>
/// Lifecycle:
/// <list type="bullet">
/// <item>Constructor creates channels and spawns the Owner actor.</item>
/// <item>Owner creates an Instance actor that materializes the graph using these channels.</item>
/// <item>On failure, Owner retries with exponential backoff (100ms, 500ms, 2s), reconnecting to the same channels.</item>
/// <item><see cref="Dispose"/> completes the request channel and sends <see cref="ClientStreamOwner.Shutdown"/> to the Owner.</item>
/// <item><see cref="DisposeAsync"/> additionally waits for the Owner actor to terminate.</item>
/// </list>
/// </para>
/// <para>
/// <b>Advanced usage:</b> For direct control over the actor-based lifecycle — including custom
/// <see cref="Akka.Actor.SupervisorStrategy"/> and actor-level monitoring — use
/// <see cref="IClientStreamOwner"/> obtained via <see cref="ClientStreamOwnerWrapper.Create"/>.
/// This class remains the recommended approach for standard usage through <see cref="ITurboHttpClientFactory"/>.
/// </para>
/// </summary>
internal sealed class TurboClientStreamManager : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IActorRef _owner;
    private int _disposed;

    internal ChannelWriter<HttpRequestMessage> Requests { get; }
    internal ChannelReader<HttpResponseMessage> Responses { get; }

    /// <summary>
    /// Exposes the actor-based owner interface for advanced scenarios such as
    /// monitoring actor lifecycle events or sending custom messages.
    /// </summary>
    internal IClientStreamOwner Owner { get; }

    /// <summary>
    /// Exposes the response-channel writer so tests can inject synthetic responses
    /// without requiring a live TCP connection.
    /// </summary>
    internal ChannelWriter<HttpResponseMessage> ResponseWriter { get; }

    public TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system)
        : this(clientOptions, requestOptionsFactory, system, PipelineDescriptor.Empty)
    {
    }

    internal TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system, PipelineDescriptor descriptor)
    {
        // Create stable channels — these survive instance actor restarts.
        // The manager owns these channels; the instance actor reads/writes but never completes them.
        var requestsChannel = Channel.CreateUnbounded<HttpRequestMessage>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        var responsesChannel = Channel.CreateUnbounded<HttpResponseMessage>(new UnboundedChannelOptions
        {
            SingleWriter = true
        });

        Requests = requestsChannel.Writer;
        Responses = responsesChannel.Reader;
        ResponseWriter = responsesChannel.Writer;

        // Spawn the Owner actor — it manages the Instance actor lifecycle,
        // tracks pending work, and handles retry with exponential backoff.
        _owner = system.ActorOf(
            Props.Create(() => new ClientStreamOwnerActor()),
            $"stream-owner-{Guid.NewGuid():N}");

        Owner = new ClientStreamOwnerWrapper(_owner);

        // Tell the Owner to create a stream instance. The instance will materialize
        // the Akka.Streams pipeline using our channels. Requests written to the channel
        // before materialization completes are buffered in the unbounded channel.
        _owner.Tell(new ClientStreamOwner.CreateStreamInstance(
            clientOptions,
            requestOptionsFactory,
            descriptor,
            requestsChannel.Reader,
            responsesChannel.Writer));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Complete the request channel — the source will finish, the pipeline
        // drains, and the instance actor stops writing to the response channel.
        Requests.TryComplete();

        // Signal the Owner to shut down gracefully. It waits for pending work
        // to drain (up to 5s), then stops the instance and itself.
        _owner.Tell(new ClientStreamOwner.Shutdown());

        // Complete the response channel so downstream consumers (DrainResponsesAsync) terminate.
        ResponseWriter.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Requests.TryComplete();

        try
        {
            // GracefulStop sends the Shutdown message and waits for the actor
            // to terminate within the timeout. Returns true on success.
            await _owner.GracefulStop(ShutdownTimeout, new ClientStreamOwner.Shutdown());
        }
        catch (TaskCanceledException)
        {
            // Timeout expired — actor didn't terminate in time. Acceptable for shutdown.
        }

        ResponseWriter.TryComplete();
    }
}
