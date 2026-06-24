using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class FlowControlledDrainSlotSpec
{
    [Fact(Timeout = 5000)]
    public void ReservedWindow_should_default_to_zero()
    {
        var slot = new FlowControlledDrainSlot();
        Assert.Equal(0, slot.ReservedWindow);
    }

    [Fact(Timeout = 5000)]
    public void ReservedWindow_should_persist_across_reads()
    {
        var slot = new FlowControlledDrainSlot();
        slot.ReservedWindow = 8 * 1024;
        Assert.Equal(8 * 1024, slot.ReservedWindow);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_ReservedWindow()
    {
        var slot = new FlowControlledDrainSlot();
        slot.ReservedWindow = 16 * 1024;

        slot.Reset();

        Assert.Equal(0, slot.ReservedWindow);
    }
}
