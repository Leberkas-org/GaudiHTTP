using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ConnectionLoggingBidiStage
    : GraphStage<BidiShape<ITransportInbound, ITransportInbound, ITransportOutbound, ITransportOutbound>>
{
    private readonly Inlet<ITransportInbound> _inboundIn = new("ConnLog.In.Inbound");
    private readonly Outlet<ITransportInbound> _inboundOut = new("ConnLog.Out.Inbound");
    private readonly Inlet<ITransportOutbound> _outboundIn = new("ConnLog.In.Outbound");
    private readonly Outlet<ITransportOutbound> _outboundOut = new("ConnLog.Out.Outbound");

    private readonly ILogger _logger;

    public ConnectionLoggingBidiStage(ILogger logger)
    {
        _logger = logger;
        Shape = new BidiShape<ITransportInbound, ITransportInbound, ITransportOutbound, ITransportOutbound>(
            _inboundIn, _inboundOut, _outboundIn, _outboundOut);
    }

    public override BidiShape<ITransportInbound, ITransportInbound, ITransportOutbound, ITransportOutbound> Shape
    {
        get;
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new ConnectionLoggingLogic(this);

    private sealed class ConnectionLoggingLogic : GraphStageLogic
    {
        private readonly ConnectionLoggingBidiStage _stage;

        public ConnectionLoggingLogic(ConnectionLoggingBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inboundIn,
                onPush: () =>
                {
                    var element = Grab(stage._inboundIn);
                    if (element is TransportData { Buffer: var buffer } && _stage._logger.IsEnabled(LogLevel.Debug))
                    {
                        var dump = HexDumpFormatter.Format(buffer.Span);
                        _stage._logger.LogDebug("ReadAsync[{Length}]{NewLine}{Dump}",
                            buffer.Length, Environment.NewLine, dump);
                    }
                    Push(stage._inboundOut, element);
                },
                onUpstreamFinish: () => Complete(stage._inboundOut),
                onUpstreamFailure: ex => Fail(stage._inboundOut, ex));

            SetHandler(stage._inboundOut,
                onPull: () => Pull(stage._inboundIn),
                onDownstreamFinish: _ => Cancel(stage._inboundIn));

            SetHandler(stage._outboundIn,
                onPush: () =>
                {
                    var element = Grab(stage._outboundIn);
                    if (element is TransportData { Buffer: var buffer } && _stage._logger.IsEnabled(LogLevel.Debug))
                    {
                        var dump = HexDumpFormatter.Format(buffer.Span);
                        _stage._logger.LogDebug("WriteAsync[{Length}]{NewLine}{Dump}",
                            buffer.Length, Environment.NewLine, dump);
                    }
                    Push(stage._outboundOut, element);
                },
                onUpstreamFinish: () => Complete(stage._outboundOut),
                onUpstreamFailure: ex => Fail(stage._outboundOut, ex));

            SetHandler(stage._outboundOut,
                onPull: () => Pull(stage._outboundIn),
                onDownstreamFinish: _ => Cancel(stage._outboundIn));
        }
    }
}
