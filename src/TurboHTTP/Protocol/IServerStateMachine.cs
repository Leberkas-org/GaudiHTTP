using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;

namespace TurboHTTP.Protocol;

internal interface IServerStateMachine
{
    bool CanAcceptResponse { get; }
    bool ShouldComplete { get; }
    bool ShouldPauseNetwork => false;
    int MaxQueuedRequests { get; }

    void PreStart();
    void OnResponse(IFeatureCollection features);
    void DecodeClientData(ITransportInbound data);
    void OnDownstreamFinished();
    void OnTimerFired(string name);
    void OnBodyMessage(object msg);
    void OnOutboundFlushed() { }
    void ResumeBody() { }
    void Cleanup();
}

