using System.Collections.Immutable;
using System.Net;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Streams.Stages.Routing;

/// <summary>
/// Custom shape for <see cref="ExtractOptionsStage"/>: two inlets (request + reuse feedback),
/// two outlets (request passthrough + connection signal).
/// </summary>
internal sealed class ExtractOptionsStage : GraphStage<FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem>>
{
    private readonly TurboClientOptions _clientOptions;
    private readonly Inlet<HttpRequestMessage> _in = new("ExtractOptions.In");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ExtractOptions.Out.Request");
    private readonly Outlet<IOutputItem> _outSignal = new("ExtractOptions.Out.Signal");

    public override FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem> Shape { get; }

    public ExtractOptionsStage(TurboClientOptions? clientOptions = null)
    {
        _clientOptions = clientOptions ?? new TurboClientOptions();
        Shape = new FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem>(_in, _outRequest, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ExtractOptionsStage _stage;
        private bool _connectItemSent;
        private bool _needsReconnect;
        private HttpRequestMessage? _pending;

        public Logic(ExtractOptionsStage stage) : base(stage.Shape)
        {
            _stage = stage;
            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);
                    Log.Debug("ExtractOptionsStage: onPush request={0} {1}, connectItemSent={2}, needsReconnect={3}",
                        request.Method, request.RequestUri, _connectItemSent, _needsReconnect);

                    if (!_connectItemSent)
                    {
                        // First request, reconnect needed, or HTTP/1.0: emit ConnectItem
                        var options =
                            TcpOptionsFactory.Build(request.RequestUri!, stage._clientOptions, request.Version);
                        _pending = request;
                        _connectItemSent = true;
                        _needsReconnect = false;
                        Log.Debug("ExtractOptionsStage: emitting ConnectItem for {0}", request.RequestUri?.Authority);
                        Push(stage._outSignal, new ConnectItem(options) { Key = RequestEndpoint.FromRequest(request) });

                        // The downstream may have already pulled _outRequest
                        // before the first element arrived (pull propagation is synchronous
                        // while Source.Queue delivery is async). Serve that demand now.
                        if (IsAvailable(stage._outRequest))
                        {
                            Log.Debug(
                                "ExtractOptionsStage: outRequest available — pushing pending request immediately");
                            Push(stage._outRequest, _pending);
                            _pending = null;
                        }
                    }
                    else
                    {
                        Push(stage._outRequest, request);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex =>
                {
                    // Absorb the failure (don't crash the pipeline) but complete the stage
                    // so downstream stages can drain and the substream shuts down cleanly.
                    Log.Warning("ExtractOptionsStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._outSignal,
                onPull: () =>
                {
                    if (!_connectItemSent && !HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                }, onDownstreamFinish: _ => { });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    if (_pending is not null)
                    {
                        Push(stage._outRequest, _pending);
                        _pending = null;
                    }
                    else if (!HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                }, onDownstreamFinish: _ => CompleteStage());
        }
    }
}