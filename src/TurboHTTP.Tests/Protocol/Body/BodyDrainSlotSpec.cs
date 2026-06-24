using System.Buffers;
using TurboHTTP.Protocol.Body;

namespace TurboHTTP.Tests.Protocol.Body;

public sealed class BodyDrainSlotSpec
{
    [Fact(Timeout = 5000)]
    public void Initialize_should_set_identity_fields()
    {
        var slot = new BodyDrainSlot<int>();
        var stream = new MemoryStream([1, 2, 3]);
        var cts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);

        slot.Initialize(42, stream, CancellationToken.None, linked);

        Assert.Equal(42, slot.StreamId);
        Assert.Same(stream, slot.BodyStream);
        Assert.Same(linked, slot.LinkedCts);
    }

    [Fact(Timeout = 5000)]
    public void BeginRead_should_set_IsReadInFlight()
    {
        var slot = new BodyDrainSlot<int>();

        slot.BeginRead();

        Assert.True(slot.IsReadInFlight);
    }

    [Fact(Timeout = 5000)]
    public void CompleteRead_should_clear_IsReadInFlight()
    {
        var slot = new BodyDrainSlot<int>();
        slot.BeginRead();

        slot.CompleteRead();

        Assert.False(slot.IsReadInFlight);
    }

    [Fact(Timeout = 5000)]
    public void MarkOrphaned_should_set_IsOrphaned()
    {
        var slot = new BodyDrainSlot<int>();

        slot.MarkOrphaned();

        Assert.True(slot.IsOrphaned);
    }

    [Fact(Timeout = 5000)]
    public void ReservedWindow_should_default_to_zero()
    {
        var slot = new BodyDrainSlot<int>();
        Assert.Equal(0, slot.ReservedWindow);
    }

    [Fact(Timeout = 5000)]
    public void ReservedWindow_should_persist_across_reads()
    {
        var slot = new BodyDrainSlot<int>();
        slot.ReservedWindow = 8 * 1024;
        Assert.Equal(8 * 1024, slot.ReservedWindow);
    }

    [Fact(Timeout = 5000)]
    public void EnsureBuffer_should_rent_from_MemoryPool()
    {
        var slot = new BodyDrainSlot<int>();

        slot.EnsureBuffer(256);

        Assert.NotNull(slot.Buffer);
        Assert.True(slot.Buffer!.Memory.Length >= 256);

        slot.DisposeResources();
    }

    [Fact(Timeout = 5000)]
    public void EnsureBuffer_should_be_idempotent()
    {
        var slot = new BodyDrainSlot<int>();
        slot.EnsureBuffer(128);
        var first = slot.Buffer;

        slot.EnsureBuffer(128);

        Assert.Same(first, slot.Buffer);

        slot.DisposeResources();
    }

    [Fact(Timeout = 5000)]
    public void EnsureBuffer_should_respect_minimum_size_of_256()
    {
        var slot = new BodyDrainSlot<int>();

        slot.EnsureBuffer(1);

        Assert.True(slot.Buffer!.Memory.Length >= 256);

        slot.DisposeResources();
    }

    [Fact(Timeout = 5000)]
    public void DisposeResources_should_dispose_buffer_and_LinkedCts()
    {
        var slot = new BodyDrainSlot<int>();
        var stream = new MemoryStream([]);
        var cts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        slot.Initialize(1, stream, CancellationToken.None, linked);
        slot.EnsureBuffer(256);

        slot.DisposeResources();

        Assert.Null(slot.Buffer);
        Assert.Null(slot.LinkedCts);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_all_state()
    {
        var slot = new BodyDrainSlot<int>();
        var stream = new MemoryStream([1, 2, 3]);
        var cts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        slot.Initialize(7, stream, CancellationToken.None, linked);
        slot.EnsureBuffer(256);
        slot.BeginRead();
        slot.MarkOrphaned();
        slot.ReservedWindow = 16 * 1024;

        slot.Reset();

        Assert.Equal(0, slot.StreamId);
        Assert.Null(slot.BodyStream);
#pragma warning disable xUnit1051 // SUT behavior: asserts slot resets RequestCt to default, not test cooperative cancellation
        Assert.Equal(default, slot.RequestCt);
#pragma warning restore xUnit1051
        Assert.Null(slot.LinkedCts);
        Assert.Null(slot.Buffer);
        Assert.Equal(0, slot.ReservedWindow);
        Assert.False(slot.IsReadInFlight);
        Assert.False(slot.IsOrphaned);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_work_for_long_streamId()
    {
        var slot = new BodyDrainSlot<long>();
        var stream = new MemoryStream([]);
        slot.Initialize(99L, stream, CancellationToken.None, null);

        slot.Reset();

        Assert.Equal(0L, slot.StreamId);
        Assert.Null(slot.BodyStream);
    }
}
