using TurboHTTP.Internal;

namespace TurboHTTP.Streams.Stages;

public interface IStageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound( IOutputItem item);
    void OnWarning(string message);
    void OnReconnectFailed();
}