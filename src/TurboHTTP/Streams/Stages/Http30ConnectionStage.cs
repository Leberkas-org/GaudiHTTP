using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Streams.Stages;

/// <summary>
/// RFC 9114 — Consolidated HTTP/3 connection stage.
///
/// Mirrors <see cref="Http20ConnectionStage"/>: a single <see cref="GraphStage{ConnectionShape}"/>
/// that owns all protocol logic via <see cref="StateMachine"/>. Handles request encoding,
/// frame decoding, response assembly, QPACK feedback, control stream preface, idle timeout,
/// and reconnection — replacing the previous 13-stage pipeline.
///
/// Ports:
/// - InServer: raw transport items (frame data + QPACK decoder stream bytes + signals)
/// - InApp: HTTP requests from the application
/// - OutResponse: assembled HTTP responses
/// - OutNetwork: serialized bytes to transport (frames + tagged QPACK/control items)
/// </summary>
public sealed class Http30ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<IInputItem> _inServer = new("Http30Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http30Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http30Connection.In.App");
    private readonly Outlet<IOutputItem> _outNetwork = new("Http30Connection.Out.Network");

    private readonly Http3ConnectionConfig _config;

    public Http30ConnectionStage(Http3ConnectionConfig config)
    {
        _config = config;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic, IStageOperations
    {
        private const string IdleCheckTimerKey = "idle-timeout-check";

        private readonly Http30ConnectionStage _stage;
        private readonly StateMachine _sm;
        private readonly List<IOutputItem> _pendingOutbound = [];
        private readonly List<HttpResponseMessage> _pendingResponses = [];
        private bool _reconnectFailed;

        public Logic(Http30ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _sm = new StateMachine(stage._config, this);

            SetHandler(stage._inServer, onPush: OnServerPush,
                onUpstreamFinish: () =>
                {
                    // Flush any partially assembled response (QUIC FIN)
                    _sm.FlushPendingResponse();
                    FlushResponses();

                    if (_sm.IsReconnecting)
                    {
                        FailStage(new HttpRequestException(
                            "TurboHTTP: HTTP/3 transport closed during reconnect."));
                        return;
                    }

                    Log.Debug("Http30ConnectionStage: Completing stage due to server inlet upstream finish.");
                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http30ConnectionStage: Server inlet upstream failure: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._outResponse, onPull: () =>
            {
                if (!HasBeenPulled(stage._inServer) && !IsClosed(stage._inServer))
                {
                    Pull(stage._inServer);
                }
            });

            SetHandler(stage._inApp, onPush: OnAppPush,
                onUpstreamFinish: () =>
                {
                    if (_sm is { HasInFlightRequests: false, IsReconnecting: false })
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http30ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                    FailStage(ex);
                });

            SetHandler(stage._outNetwork, onPull: OnNetworkPull);
        }

        public override void PreStart()
        {
            ScheduleIdleCheck();
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key || key != IdleCheckTimerKey)
            {
                return;
            }

            var goAway = _sm.CheckIdleTimeout();
            if (goAway is not null)
            {
                // Serialize and emit the GOAWAY frame
                var buf = NetworkBuffer.Rent(goAway.SerializedSize);
                var span = buf.FullMemory.Span;
                goAway.WriteTo(ref span);
                buf.Length = goAway.SerializedSize;
                _pendingOutbound.Add(new Http3OutputTaggedItem(buf, OutputStreamType.Control));
                FlushOutbound();
                CompleteStage();
                return;
            }

            ScheduleIdleCheck();
        }

        void IStageOperations.OnResponse(HttpResponseMessage response)
        {
            _pendingResponses.Add(response);
        }

        void IStageOperations.OnOutbound(IOutputItem item)
        {
            _pendingOutbound.Add(item);
        }

        void IStageOperations.OnWarning(string message)
        {
            Log.Warning("Http30ConnectionStage: {0}", message);
        }

        void IStageOperations.OnReconnectFailed()
        {
            _reconnectFailed = true;
        }

        private void OnServerPush()
        {
            var item = Grab(_stage._inServer);

            switch (item)
            {
                // Reconnect: new connection ready — replay buffered requests
                case ConnectedSignalItem:
                {
                    _sm.OnConnectionRestored();
                    FlushOutbound();
                    TryPullRequest();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Request stream FIN — server finished sending the response.
                // Flush the pending response (body is delimited by FIN) and continue
                // accepting new requests on the same QUIC connection.
                case QuicCloseItem { Kind: QuicCloseKind.RequestStreamComplete } close:
                {
                    if (close.StreamId >= 0)
                    {
                        _sm.FlushPendingResponse(close.StreamId);
                    }
                    else
                    {
                        _sm.FlushPendingResponse();
                    }

                    FlushResponses();
                    TryPullRequest();
                    return;
                }
                // Reconnect: connection dropped again while already reconnecting
                case QuicCloseItem when _sm.IsReconnecting:
                {
                    _sm.OnReconnectAttemptFailed();
                    if (_reconnectFailed)
                    {
                        FailStage(new HttpRequestException(
                            "TurboHTTP: HTTP/3 reconnect failed after max attempts."));
                        return;
                    }

                    FlushOutbound();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // Abrupt close with in-flight requests — reconnect
                case QuicCloseItem when _sm.HasInFlightRequests:
                {
                    _sm.OnConnectionLost();
                    FlushOutbound();
                    if (!HasBeenPulled(_stage._inServer) && !IsClosed(_stage._inServer))
                    {
                        Pull(_stage._inServer);
                    }

                    return;
                }
                // QuicCloseItem with no in-flight — complete normally
                case QuicCloseItem:
                    CompleteStage();
                    return;
            }

            // QPACK decoder stream — route to encoder feedback
            if (item is Http3InputTaggedItem { StreamType: InputStreamType.QpackDecoder } tagged)
            {
                var data = (NetworkBuffer)tagged.Inner;
                _sm.ProcessQpackDecoderBytes(data.Memory);
                data.Dispose();
                Pull(_stage._inServer);
                return;
            }

            // Frame data — decode, process, and assemble responses
            if (item is not Http3InputTaggedItem { Inner: NetworkBuffer buffer } taggedData)
            {
                if (item is NetworkBuffer rawBuffer)
                {
                    ProcessFrameData(rawBuffer, streamId: 0);
                    return;
                }

                Pull(_stage._inServer);
                return;
            }

            ProcessFrameData(buffer, taggedData.StreamId);
        }

        private void ProcessFrameData(NetworkBuffer buffer, long streamId)
        {
            var frames = _sm.DecodeServerData(buffer);

            var anyProcessed = false;
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                anyProcessed = true;

                var forwarded = _sm.ProcessFrame(frame);
                if (forwarded is not null)
                {
                    _sm.AssembleResponse(forwarded, streamId);
                }
            }

            if (!anyProcessed)
            {
                Pull(_stage._inServer);
                return;
            }

            FlushOutbound();
            FlushResponses();
            TryPullRequest();
        }

        private void OnAppPush()
        {
            var request = Grab(_stage._inApp);
            _sm.EncodeRequest(request);
            FlushOutbound();
            TryPullRequest();
        }

        private void OnNetworkPull()
        {
            var preface = _sm.TryBuildControlPreface();
            if (preface is not null)
            {
                Push(_stage._outNetwork, preface);
                return;
            }

            TryPullRequest();
        }

        private void FlushResponses()
        {
            if (_pendingResponses.Count == 0)
            {
                if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequests)
                {
                    CompleteStage();
                    return;
                }

                Pull(_stage._inServer);
                return;
            }

            if (_pendingResponses.Count == 1 && IsAvailable(_stage._outResponse))
            {
                var response = _pendingResponses[0];
                _pendingResponses.Clear();
                Push(_stage._outResponse, response);

                if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequests)
                {
                    CompleteStage();
                    return;
                }

                Pull(_stage._inServer);
                return;
            }

            EmitMultiple(_stage._outResponse, _pendingResponses.ToArray(),
                () =>
                {
                    if (IsClosed(_stage._inApp) && !_sm.HasInFlightRequests)
                    {
                        CompleteStage();
                        return;
                    }

                    Pull(_stage._inServer);
                });
            _pendingResponses.Clear();
        }

        private void FlushOutbound()
        {
            if (_pendingOutbound.Count == 0)
            {
                return;
            }

            if (_pendingOutbound.Count == 1 && IsAvailable(_stage._outNetwork))
            {
                var outItem = _pendingOutbound[0];
                _pendingOutbound.Clear();
                Push(_stage._outNetwork, outItem);
                return;
            }

            EmitMultiple(_stage._outNetwork, _pendingOutbound.ToArray());
            _pendingOutbound.Clear();
        }

        private void TryPullRequest()
        {
            if (_sm.CanAcceptRequest
                && !HasBeenPulled(_stage._inApp)
                && !IsClosed(_stage._inApp))
            {
                Pull(_stage._inApp);
            }
        }

        private void ScheduleIdleCheck()
        {
            if (_sm.IsTimeoutDisabled)
            {
                return;
            }

            var remaining = _sm.TimeUntilExpiry();
            var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
            ScheduleOnce(IdleCheckTimerKey, checkInterval);
        }

        public override void PostStop()
        {
            _sm.Dispose();
        }
    }
}
