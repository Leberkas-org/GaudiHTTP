using Akka.Actor;

namespace GaudiHTTP.Protocol.Body;

internal interface IBodyDrainTarget
{
    IActorRef StageActor { get; }
    void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream);
    void OnDrainComplete(int streamId);
    void OnDrainFailed(int streamId, Exception reason);
}

internal interface IBodyDrainTarget<TStreamId>
{
    IActorRef PipeToTarget { get; }
    bool HasPendingDemand { get; }
    int PreferredChunkSize { get; }
    void EmitDataFrames(TStreamId streamId, ReadOnlyMemory<byte> data, bool endStream);
    void OnDrainComplete(TStreamId streamId);
    void OnDrainFailed(TStreamId streamId, Exception reason);
}
