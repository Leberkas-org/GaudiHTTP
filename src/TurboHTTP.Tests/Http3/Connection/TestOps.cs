using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Http3.Connection;

internal sealed class TestOps : IStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<IOutputItem> OutboundItems { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool ReconnectFailed { get; private set; }

    public void OnResponse(HttpResponseMessage response) => Responses.Add(response);
    public void OnOutbound(IOutputItem item) => OutboundItems.Add(item);
    public void OnWarning(string message) => Warnings.Add(message);
    public void OnReconnectFailed() => ReconnectFailed = true;
}
