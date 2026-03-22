using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Servus.Akka;

namespace TurboHttp.Transport;

public sealed class ClientManager : ReceiveActor
{
    public record CreateRunner(TcpOptions Options, IActorRef Handler, IClientProvider? StreamProvider = null);

    public record CreateRunnerWithChannels(
        TcpOptions Options,
        IActorRef Handler,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)> InboundChannel,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)> OutboundChannel,
        IClientProvider? StreamProvider = null,
        StreamDirection Direction = StreamDirection.Bidirectional,
        Func<CancellationToken, Task<Stream>>? StreamFactory = null)
        : CreateRunner(Options, Handler, StreamProvider);

    public ClientManager()
    {
        Receive<CreateRunner>(Handle);
        Receive<Terminated>(Handle);
    }

    private static void Handle(CreateRunner msg)
    {
#pragma warning disable CA1416 // QuicClientProvider is guarded by QuicOptions at runtime
        var provider = msg.StreamProvider ?? msg.Options switch
        {
            QuicOptions quic => new QuicClientProvider(quic),
            TlsOptions tls => new TlsClientProvider(tls),
            _ => new TcpClientProvider(msg.Options)
        };
#pragma warning restore CA1416
        var prefix = msg.Options switch
        {
            QuicOptions => "QUIC",
            TlsOptions => "TLS",
            _ => "TCP"
        };
        var host = msg.Options.Host;
        var port = msg.Options.Port;
        var name = $"tcp-runner-{prefix}-{host.Replace(".", "-")}-{port}-{Guid.NewGuid()}";
        IActorRef runner;
        if (msg is CreateRunnerWithChannels msgWithChannels)
        {
            // Use Props.Create directly to avoid ActivatorUtilities type-matching
            // issues with nullable/default constructor parameters (StreamFactory, Direction).
            var inbound = msgWithChannels.InboundChannel;
            var outbound = msgWithChannels.OutboundChannel;
            var direction = msgWithChannels.Direction;
            var streamFactory = msgWithChannels.StreamFactory;
            var maxFrame = msg.Options.MaxFrameSize;
            var handler = msg.Handler;
            runner = Context.ActorOf(
                Props.Create(() => new ClientRunner(
                    provider, handler, maxFrame, inbound, outbound, direction, streamFactory)),
                name);
        }
        else
        {
            runner = Context
                .ResolveChildActor<ClientRunner>(name, provider, msg.Handler,
                    msg.Options.MaxFrameSize);
        }

        Context.Watch(runner);
    }

    private static void Handle(Terminated msg)
    {
        Context.GetLogger().Error("Client dead: {0}", msg.ActorRef.Path);
    }
}