using System.Net;
using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHTTP.Server.Streams.Stages;

internal sealed class ServerExceptionHandlerStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("ExceptionHandler.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ExceptionHandler.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("ExceptionHandler.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("ExceptionHandler.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public ServerExceptionHandlerStage()
    {
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    public BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed> CreateBidiFlow()
    {
        return BidiFlow.FromGraph(this);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ServerExceptionHandlerStage _stage;

        public Logic(ServerExceptionHandlerStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inRequest,
                onPush: () => Push(stage._outRequest, Grab(stage._inRequest)),
                onUpstreamFinish: () => Complete(stage._outRequest));

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            SetHandler(stage._inResponse,
                onPush: () => Push(stage._outResponse, Grab(stage._inResponse)),
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex =>
                {
                    Log.Error(ex, "ServerExceptionHandlerStage: downstream failure — returning 500");
                    if (IsAvailable(stage._outResponse))
                    {
                        Push(stage._outResponse, new HttpResponseMessage(HttpStatusCode.InternalServerError));
                    }

                    CompleteStage();
                });

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    if (!HasBeenPulled(stage._inResponse) && !IsClosed(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                },
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }
    }
}
