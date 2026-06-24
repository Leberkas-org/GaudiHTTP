using Servus.Akka.Transport;

namespace GaudiHTTP.Protocol;

internal interface IClientStateMachine
{
    bool CanAcceptRequest { get; }
    bool HasInFlightRequests { get; }
    bool IsReconnecting { get; }
    bool ShouldPauseNetwork => false;

    void PreStart();
    void OnRequest(HttpRequestMessage request);
    void OnRequestCancelled(HttpRequestMessage request) { }
    void DecodeServerData(ITransportInbound data);
    void OnUpstreamFinished();
    void OnTimerFired(string name);
    void OnBodyMessage(object msg);
    void OnOutboundFlushed() { }
    void Cleanup();
}
