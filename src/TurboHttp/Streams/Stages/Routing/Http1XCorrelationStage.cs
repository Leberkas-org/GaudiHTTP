using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;

namespace TurboHttp.Streams.Stages.Routing;

public sealed class Http1XCorrelationShape : Shape
{
    public Inlet<HttpRequestMessage> InRequest { get; }
    public Inlet<HttpResponseMessage> InResponse { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Outlet<IControlItem> OutControl { get; }

    public Http1XCorrelationShape(
        Inlet<HttpRequestMessage> inRequest,
        Inlet<HttpResponseMessage> inResponse,
        Outlet<HttpResponseMessage> outResponse,
        Outlet<IControlItem> outControl)
    {
        InRequest = inRequest;
        InResponse = inResponse;
        OutResponse = outResponse;
        OutControl = outControl;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [InRequest, InResponse];

    public override ImmutableArray<Outlet> Outlets =>
        [OutResponse, OutControl];

    public override Shape DeepCopy()
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)InRequest.CarbonCopy(),
            (Inlet<HttpResponseMessage>)InResponse.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Outlet<IControlItem>)OutControl.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http1XCorrelationShape(
            (Inlet<HttpRequestMessage>)inlets[0],
            (Inlet<HttpResponseMessage>)inlets[1],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Outlet<IControlItem>)outlets[1]);
    }
}

internal sealed class Http1XCorrelationStage : GraphStage<Http1XCorrelationShape>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Http1XCorrelation.In.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Http1XCorrelation.In.Response");
    private readonly Outlet<HttpResponseMessage> _out = new("Http1XCorrelation.Out");
    private readonly Outlet<IControlItem> _outSignal = new("Http1XCorrelation.Out.Signal");

    public override Http1XCorrelationShape Shape { get; }

    public Http1XCorrelationStage()
    {
        Shape = new Http1XCorrelationShape(_inRequest, _inResponse, _out, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http1XCorrelationStage _stage;
        private HttpRequestMessage? _inFlightRequest;
        private bool _requestUpstreamFinished;
        private bool _responseUpstreamFinished;

        public Logic(Http1XCorrelationStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    _inFlightRequest = Grab(stage._inRequest);
                    var key = RequestEndpoint.FromRequest(_inFlightRequest);
                    Emit(stage._outSignal, new StreamAcquireItem { Key = key });
                    if (_responseUpstreamFinished)
                    {
                        // Response stream is done; this request can never be fulfilled.
                        CompleteStage();
                        return;
                    }
                    if (!HasBeenPulled(stage._inResponse))
                    {
                        Pull(stage._inResponse);
                    }
                },
                onUpstreamFinish: () =>
                {
                    _requestUpstreamFinished = true;
                    TryComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http1XCorrelationStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    response.RequestMessage = _inFlightRequest;
                    _inFlightRequest = null;
                    Push(stage._out, response);
                },
                onUpstreamFinish: () =>
                {
                    _responseUpstreamFinished = true;
                    TryComplete();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http1XCorrelationStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_inFlightRequest == null && !IsClosed(stage._inRequest) && !HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }
                });

            SetHandler(stage._outSignal, onPull: () =>
            {
                // Demand-driven by Emit; no action needed.
            });
        }

        private void TryComplete()
        {
            // If the response stream ended while a request is in flight, the request will
            // never receive a response — complete the stage gracefully to avoid deadlock.
            if (_responseUpstreamFinished && _inFlightRequest != null)
            {
                CompleteStage();
                return;
            }
            if (_requestUpstreamFinished && _responseUpstreamFinished && _inFlightRequest == null)
            {
                CompleteStage();
            }
        }
    }
}
