using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace TurboHttp.Client;

/// <summary>
/// Public interface for advanced users who want to interact with the actor-based
/// stream lifecycle directly. Provides access to the underlying <see cref="ClientStreamOwnerActor"/>
/// and allows customized stream initialization with user-supplied supervision strategies.
/// <para>
/// For most use cases, prefer <see cref="ITurboHttpClient"/> with <see cref="ITurboHttpClientFactory"/>.
/// Use this interface when you need to:
/// <list type="bullet">
/// <item>Customize the <see cref="SupervisorStrategy"/> for stream instance supervision</item>
/// <item>Monitor actor lifecycle events directly</item>
/// <item>Integrate with an existing Akka.NET actor system</item>
/// </list>
/// </para>
/// </summary>
public interface IClientStreamOwner
{
    /// <summary>
    /// Initializes a new stream instance with the given options. The owner actor
    /// spawns a child <c>ClientStreamInstance</c> that materializes the Akka.Streams
    /// pipeline. On success, returns a <see cref="StreamInitializationResult.Success"/>
    /// containing the instance actor ref. On failure (after retry exhaustion),
    /// returns <see cref="StreamInitializationResult.Failed"/>.
    /// </summary>
    /// <param name="options">Configuration for the stream instance including client options and optional supervisor strategy.</param>
    /// <param name="ct">Cancellation token to abort the initialization.</param>
    /// <returns>A result indicating success or failure of stream initialization.</returns>
    Task<StreamInitializationResult> InitializeStreamAsync(StreamInitializationOptions options, CancellationToken ct);

    /// <summary>
    /// The underlying <see cref="ClientStreamOwnerActor"/> actor reference.
    /// Advanced users can send messages directly using the protocol defined in
    /// <see cref="ClientStreamOwner"/>.
    /// </summary>
    IActorRef ActorRef { get; }
}
