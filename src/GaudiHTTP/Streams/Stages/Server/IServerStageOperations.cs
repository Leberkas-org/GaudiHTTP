using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Pooling;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Streams.Stages.Server;

internal interface IServerStageOperations
{
    void OnRequest(IFeatureCollection features);
    void OnOutbound(ITransportOutbound item);
    void OnScheduleTimer(string name, TimeSpan delay);
    void OnCancelTimer(string name);
    ILoggingAdapter Log { get; }
    IActorRef StageActor { get; }
    IMaterializer Materializer { get; }
    IServiceProvider? Services => null;
    GaudiHttpConnectionFeature? ConnectionFeature => null;
    TlsHandshakeFeature? TlsHandshakeFeature => null;
    ConnectionObjectPool? PoolContext => null;
    void OnResponseBodyComplete(IFeatureCollection features) { }
    bool HasPendingDemand => false;
}