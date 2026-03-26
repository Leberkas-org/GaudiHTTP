using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using TurboHttp.Streams;

namespace TurboHttp.Client;

/// <summary>
/// Default implementation of <see cref="IClientStreamOwner"/> that wraps a
/// <see cref="ClientStreamOwnerActor"/>. Created by <see cref="TurboClientStreamManager"/>
/// or directly via <see cref="Create"/> for advanced scenarios.
/// </summary>
internal sealed class ClientStreamOwnerWrapper : IClientStreamOwner
{
    private static readonly TimeSpan DefaultAskTimeout = TimeSpan.FromSeconds(30);

    public IActorRef ActorRef { get; }

    internal ClientStreamOwnerWrapper(IActorRef ownerActor)
    {
        ActorRef = ownerActor;
    }

    /// <summary>
    /// Creates a new <see cref="IClientStreamOwner"/> by spawning a <see cref="ClientStreamOwnerActor"/>
    /// in the given actor system.
    /// </summary>
    internal static IClientStreamOwner Create(ActorSystem system, SupervisorStrategy? customStrategy = null)
    {
        var props = Props.Create(() => new ClientStreamOwnerActor());
        if (customStrategy is not null)
        {
            props = props.WithSupervisorStrategy(customStrategy);
        }

        var owner = system.ActorOf(props, $"stream-owner-{Guid.NewGuid():N}");
        return new ClientStreamOwnerWrapper(owner);
    }

    public async Task<StreamInitializationResult> InitializeStreamAsync(
        StreamInitializationOptions options, CancellationToken ct)
    {
        var requestOptionsFactory = options.RequestOptionsFactory;
        var clientOptions = options.ClientOptions;

        // Create channels for the stream
        var requestsChannel = System.Threading.Channels.Channel.CreateUnbounded<System.Net.Http.HttpRequestMessage>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        var responsesChannel = System.Threading.Channels.Channel.CreateUnbounded<System.Net.Http.HttpResponseMessage>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleWriter = true });

        var createMsg = new ClientStreamOwner.CreateStreamInstance(
            clientOptions,
            requestOptionsFactory,
            PipelineDescriptor.Empty,
            requestsChannel.Reader,
            responsesChannel.Writer);

        try
        {
            var response = await ActorRef.Ask(createMsg, DefaultAskTimeout, ct);

            return response switch
            {
                ClientStreamOwner.StreamInstanceCreated created => new StreamInitializationResult.Success(created.InstanceRef),
                ClientStreamOwner.StreamInstanceFailed failed => new StreamInitializationResult.Failed(failed.Reason),
                _ => new StreamInitializationResult.Failed(
                    new InvalidOperationException($"Unexpected response type: {response.GetType().Name}"))
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StreamInitializationResult.Failed(ex);
        }
    }
}
