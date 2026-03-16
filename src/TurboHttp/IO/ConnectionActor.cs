using System;
using System.Buffers;
using Akka.Actor;
using Akka.Event;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class ConnectionActor : ReceiveActor
{
    /// <summary>
    /// Sent to the parent actor when a TCP connection is established,
    /// providing direct Channel-based I/O access via <see cref="ConnectionHandle"/>.
    /// </summary>
    public sealed record ConnectionReady(ConnectionHandle Handle);

    private readonly TcpOptions _options;
    private readonly IActorRef _clientManager;
    private readonly HostKey _hostKey;

    private System.Threading.Channels.ChannelWriter<(IMemoryOwner<byte>, int)>? _outbound;
    private System.Threading.Channels.ChannelReader<(IMemoryOwner<byte>, int)>? _inbound;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private IActorRef? _runner;

    public ConnectionActor(TcpOptions options, IActorRef clientManager, HostKey hostKey = default)
    {
        _options = options;
        _clientManager = clientManager;
        _hostKey = hostKey;

        Receive<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<Terminated>(HandleTerminated);
    }

    protected override void PreStart()
    {
        Connect();
    }

    private void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateTcpRunner(_options, Self));
    }

    private void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _inbound = msg.InboundReader;
        _outbound = msg.OutboundWriter;
        _runner = Sender;

        Context.Watch(_runner);

        // Send ConnectionReady with direct channel handles to parent
        var handle = new ConnectionHandle(msg.OutboundWriter, msg.InboundReader, _hostKey, Self);
        Context.Parent.Tell(new ConnectionReady(handle));
    }

    private void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("Disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private void HandleTerminated(Terminated msg)
    {
        if (!msg.ActorRef.Equals(_runner)) return;
        _log.Warning("ClientRunner terminated");
        Reconnect();
    }

    private void Reconnect()
    {
        _runner = null;
        _outbound = null;
        _inbound = null;

        Connect();
    }

    protected override void PostStop()
    {
        try
        {
            _runner?.Tell(new DoClose());
        }
        catch
        {
            // noop
        }
    }
}
