using Akka.Event;

namespace TurboHTTP.Streams.Stages.Features;

internal interface IFeatureStageOperations
{
    void OnPushRequest(HttpRequestMessage request);
    void OnPushResponse(HttpResponseMessage response);
    void OnSignalPullRequest();
    void OnSignalPullResponse();
    void OnCompleteStage();
    void OnScheduleTimer(string key, TimeSpan delay);
    void OnCancelTimer(string key);
    ILoggingAdapter Log { get; }
}
