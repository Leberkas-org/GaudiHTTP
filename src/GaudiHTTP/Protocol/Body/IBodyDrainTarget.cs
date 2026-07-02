using System.Buffers;
using Akka.Actor;

namespace GaudiHTTP.Protocol.Body;

internal interface IBodyDrainTarget
{
    IActorRef StageActor { get; }
    void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream);

    void EmitOwnedDataFrames(int streamId, IMemoryOwner<byte> owner, int bytesWritten, bool endStream)
    {
        EmitDataFrames(streamId, owner.Memory[..bytesWritten], endStream);
        owner.Dispose();
    }

    void OnDrainComplete(int streamId);
    void OnDrainFailed(int streamId, Exception reason);
}
