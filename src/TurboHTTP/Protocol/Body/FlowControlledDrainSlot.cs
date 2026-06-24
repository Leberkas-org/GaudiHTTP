using TurboHTTP.Pooling;

namespace TurboHTTP.Protocol.Body;

internal sealed class FlowControlledDrainSlot : BodyDrainSlot<int>, IResettable
{
    public int ReservedWindow { get; set; }
    public bool HasLimbo { get; private set; }
    public ReadOnlyMemory<byte> LimboData { get; private set; }

    public void StoreLimbo(ReadOnlyMemory<byte> data)
    {
        LimboData = data;
        HasLimbo = true;
    }

    public void ShrinkLimbo(int consumed)
    {
        LimboData = LimboData[consumed..];
    }

    public void ClearLimbo()
    {
        LimboData = default;
        HasLimbo = false;
    }

    public override void Reset()
    {
        base.Reset();
        ReservedWindow = 0;
        LimboData = default;
        HasLimbo = false;
    }
}
