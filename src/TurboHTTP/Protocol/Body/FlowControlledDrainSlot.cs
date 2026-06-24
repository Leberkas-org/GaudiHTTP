using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

internal sealed class FlowControlledDrainSlot : BodyDrainSlot<int>, IResettable
{
    public int ReservedWindow { get; set; }

    public override void Reset()
    {
        base.Reset();
        ReservedWindow = 0;
    }
}
