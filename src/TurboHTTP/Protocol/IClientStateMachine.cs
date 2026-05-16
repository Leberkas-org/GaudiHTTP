using Servus.Akka.Transport;

namespace TurboHTTP.Protocol;

internal interface IClientStateMachine
{
    bool CanAcceptRequest { get; }
    bool HasInFlightRequests { get; }
    bool IsReconnecting { get; }

    void PreStart();
    void OnRequest(HttpRequestMessage request);
    void DecodeServerData(ITransportInbound data);
    void OnUpstreamFinished();
    void OnTimerFired(string name);
    void OnBodyMessage(object msg);
    void Cleanup();
}
