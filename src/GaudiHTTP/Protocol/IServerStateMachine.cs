using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;

namespace TurboHTTP.Protocol;

internal interface IServerStateMachine
{
    bool CanAcceptResponse { get; }
    bool ShouldComplete { get; }
    bool ShouldPauseNetwork => false;
    int MaxQueuedRequests { get; }

    /// <summary>
    /// Maximum number of requests that may be dispatched to the application handler concurrently
    /// on this connection. HTTP/1.x returns 1 so the connection stage serializes handler dispatch
    /// and the shared (completion-ordered) <c>ApplicationBridgeStage</c> can never reorder responses
    /// — RFC 9112 §9.3.2 requires pipelined responses in request order. Multiplexed protocols
    /// (HTTP/2, HTTP/3) route responses by stream id and leave this unbounded.
    /// </summary>
    int MaxConcurrentRequests => int.MaxValue;

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

