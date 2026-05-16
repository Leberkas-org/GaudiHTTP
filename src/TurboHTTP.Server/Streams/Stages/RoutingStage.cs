using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Server.Streams.Stages;

internal sealed class RoutingStage : GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>
{
    private readonly RouteTable _routeTable;
    private readonly TurboConnectionInfo _connectionInfo;
    private readonly IServiceProvider _services;
    private readonly CancellationToken _connectionAborted;

    private readonly Inlet<HttpRequestMessage> _in = new("Routing.In");
    private readonly Outlet<HttpResponseMessage> _out = new("Routing.Out");

    public override FlowShape<HttpRequestMessage, HttpResponseMessage> Shape { get; }

    public RoutingStage(
        RouteTable routeTable,
        TurboConnectionInfo connectionInfo,
        IServiceProvider services,
        CancellationToken connectionAborted)
    {
        _routeTable = routeTable;
        _connectionInfo = connectionInfo;
        _services = services;
        _connectionAborted = connectionAborted;
        Shape = new FlowShape<HttpRequestMessage, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly RoutingStage _stage;
        private Action<HttpResponseMessage>? _asyncCallback;

        public Logic(RoutingStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(_stage._in,
                onPush: OnPush,
                onUpstreamFinish: CompleteStage);

            SetHandler(_stage._out,
                onPull: () => Pull(_stage._in));
        }

        public override void PreStart()
        {
            _asyncCallback = GetAsyncCallback<HttpResponseMessage>(OnHandlerComplete);
        }

        private void OnPush()
        {
            var request = Grab(_stage._in);
            var method = request.Method.Method;
            var path = request.RequestUri?.AbsolutePath ?? "/";

            var match = _stage._routeTable.Match(method, path);

            if (match is { IsMatch: true, Handler: not null })
            {
                var ctx = new TurboHttpContext(
                    request,
                    _stage._connectionInfo,
                    Source.Empty<ReadOnlyMemory<byte>>(),
                    _stage._connectionAborted);
                ctx.RequestServices = _stage._services;

                foreach (var kv in match.RouteValues)
                {
                    ctx.RouteValues[kv.Key] = kv.Value;
                }

                var callback = _asyncCallback!;
                var task = match.Handler(ctx);
                if (task.IsCompleted)
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        Push(_stage._out, task.Result);
                    }
                    else
                    {
                        Push(_stage._out, new HttpResponseMessage(HttpStatusCode.InternalServerError));
                    }
                }
                else
                {
                    _ = task.ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            callback(t.Result);
                        }
                        else
                        {
                            callback(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                        }
                    });
                }
            }
            else
            {
                Push(_stage._out, new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private void OnHandlerComplete(HttpResponseMessage response)
        {
            Push(_stage._out, response);
        }
    }
}
