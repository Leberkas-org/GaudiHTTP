using Akka.Actor;

namespace GaudiHTTP.Protocol.Body;

internal interface IMultiplexedBodyDrainTarget
{
    IActorRef StageActor { get; }
    void EmitDataFrames(long streamId, ReadOnlyMemory<byte> data, bool endStream);
    void OnDrainComplete(long streamId);
    void OnDrainFailed(long streamId, Exception reason);
}
