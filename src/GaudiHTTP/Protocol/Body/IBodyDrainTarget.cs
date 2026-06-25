using Akka.Actor;

namespace GaudiHTTP.Protocol.Body;

internal interface IBodyDrainTarget
{
    IActorRef StageActor { get; }
    void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream);
    void OnDrainComplete(int streamId);
    void OnDrainFailed(int streamId, Exception reason);
}
