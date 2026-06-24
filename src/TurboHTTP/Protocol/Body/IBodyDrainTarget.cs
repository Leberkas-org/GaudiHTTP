using Akka.Actor;

namespace TurboHTTP.Protocol.Body;

internal interface IBodyDrainTarget<TStreamId>
{
    IActorRef PipeToTarget { get; }
    bool HasPendingDemand { get; }
    int PreferredChunkSize { get; }
    void EmitDataFrames(TStreamId streamId, ReadOnlyMemory<byte> data, bool endStream);
    void OnDrainComplete(TStreamId streamId);
    void OnDrainFailed(TStreamId streamId, Exception reason);
}
