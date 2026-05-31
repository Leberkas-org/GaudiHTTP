using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ListenerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IListenerFactory _factory;
    private readonly ListenerOptions _listenerOptions;
    private readonly TurboServerOptions _serverOptions;
    private readonly PipelineHandles _pipelineHandles;
    private readonly IServerProtocolEngine _engine;

    public sealed record StartListening;

    internal sealed record ListeningStarted(int BoundPort, ConnectionStageHandle Handle);

    public ListenerActor(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        TurboServerOptions serverOptions,
        PipelineHandles pipelineHandles,
        IServerProtocolEngine engine)
    {
        _factory = factory;
        _listenerOptions = listenerOptions;
        _serverOptions = serverOptions;
        _pipelineHandles = pipelineHandles;
        _engine = engine;

        Receive<StartListening>(_ => OnStartListening());
    }

    private void OnStartListening()
    {
        _log.Info("Listener starting on {0}:{1}", _listenerOptions.Host, _listenerOptions.Port);

        var listenerSource = _factory.Bind(_listenerOptions);
        var completionTcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionStage = new ConnectionStage(_serverOptions, _pipelineHandles, _engine);
        var materializer = Context.Materializer();

        var sender = Sender;

        var (boundTask, acceptSwitch) = listenerSource
            .ViaMaterialized(KillSwitches.Single<Flow<ITransportOutbound, ITransportInbound, NotUsed>>(), Keep.Both)
            .Via(connectionStage.CreateFlow(completionTcs))
            .To(Sink.Ignore<NotUsed>())
            .Run(materializer);

        var handle = new ConnectionStageHandle(acceptSwitch, connectionStage.DrainSwitch, completionTcs.Task);

        boundTask.PipeTo(sender,
            success: port => new ListeningStarted(port, handle),
            failure: ex =>
            {
                _log.Error(ex, "Failed to bind on {0}:{1}", _listenerOptions.Host, _listenerOptions.Port);
                throw ex;
            });
    }

    public static Props Create(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        TurboServerOptions serverOptions,
        PipelineHandles pipelineHandles,
        IServerProtocolEngine engine)
        => Props.Create(() => new ListenerActor(
            factory, listenerOptions, serverOptions, pipelineHandles, engine));
}
