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

        slot.Initialize(42, stream, contentLength: 3, CancellationToken.None, linked);

        Assert.Equal(42, slot.StreamId);
        Assert.Same(stream, slot.BodyStream);
        Assert.Equal(3L, slot.ContentLength);
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
    public void CompleteSyncRead_should_clear_IsReadInFlight()
    {
        var slot = new BodyDrainSlot<int>();
        slot.BeginRead();

        slot.CompleteSyncRead();

        Assert.False(slot.IsReadInFlight);
    }

    [Fact(Timeout = 5000)]
    public void CompleteAsyncRead_should_clear_IsReadInFlight_and_reset_sync_counter()
    {
        var slot = new BodyDrainSlot<int>();
        slot.BeginRead();
        slot.IncrementSyncReads(100);
        slot.IncrementSyncReads(100);

        slot.CompleteAsyncRead();

        Assert.False(slot.IsReadInFlight);
        Assert.Equal(0, slot.ConsecutiveSyncReads);
    }

    [Fact(Timeout = 5000)]
    public void IncrementSyncReads_should_return_false_below_threshold()
    {
        var slot = new BodyDrainSlot<int>();

        var result = slot.IncrementSyncReads(3);

        Assert.False(result);
        Assert.Equal(1, slot.ConsecutiveSyncReads);
    }

    [Fact(Timeout = 5000)]
    public void IncrementSyncReads_should_return_true_at_threshold()
    {
        var slot = new BodyDrainSlot<int>();

        slot.IncrementSyncReads(3);
        slot.IncrementSyncReads(3);
        var result = slot.IncrementSyncReads(3);

        Assert.True(result);
        Assert.Equal(3, slot.ConsecutiveSyncReads);
    }

    [Fact(Timeout = 5000)]
    public void ResetSyncReads_should_zero_counter()
    {
        var slot = new BodyDrainSlot<int>();
        slot.IncrementSyncReads(100);
        slot.IncrementSyncReads(100);

        slot.ResetSyncReads();

        Assert.Equal(0, slot.ConsecutiveSyncReads);
    }

    [Fact(Timeout = 5000)]
    public void MarkOrphaned_should_set_IsOrphaned()
    {
        var slot = new BodyDrainSlot<int>();

        slot.MarkOrphaned();

        Assert.True(slot.IsOrphaned);
    }

    [Fact(Timeout = 5000)]
    public void MarkDrainComplete_should_set_IsDrainComplete()
    {
        var slot = new BodyDrainSlot<int>();

        slot.MarkDrainComplete();

        Assert.True(slot.IsDrainComplete);
    }

    [Fact(Timeout = 5000)]
    public void StoreLimbo_should_set_HasLimbo_and_LimboData()
    {
        var slot = new BodyDrainSlot<int>();
        var data = new byte[] { 10, 20, 30 };

        slot.StoreLimbo(data);

        Assert.True(slot.HasLimbo);
        Assert.Equal(data, slot.LimboData.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void ShrinkLimbo_should_advance_slice()
    {
        var slot = new BodyDrainSlot<int>();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        slot.StoreLimbo(data);

        slot.ShrinkLimbo(2);

        Assert.Equal([3, 4, 5], slot.LimboData.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void ClearLimbo_should_clear_HasLimbo_and_LimboData()
    {
        var slot = new BodyDrainSlot<int>();
        slot.StoreLimbo(new byte[] { 1, 2 });

        slot.ClearLimbo();

        Assert.False(slot.HasLimbo);
        Assert.True(slot.LimboData.IsEmpty);
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
        slot.Initialize(1, stream, null, CancellationToken.None, linked);
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
        slot.Initialize(7, stream, 3, CancellationToken.None, linked);
        slot.EnsureBuffer(256);
        slot.BeginRead();
        slot.MarkOrphaned();
        slot.StoreLimbo(new byte[] { 9 });
        slot.MarkDrainComplete();
        slot.IncrementSyncReads(100);

        slot.Reset();

        Assert.Equal(0, slot.StreamId);
        Assert.Null(slot.BodyStream);
        Assert.Null(slot.ContentLength);
#pragma warning disable xUnit1051 // SUT behavior: asserts slot resets RequestCt to default, not test cooperative cancellation
        Assert.Equal(default, slot.RequestCt);
#pragma warning restore xUnit1051
        Assert.Null(slot.LinkedCts);
        Assert.Null(slot.Buffer);
        Assert.False(slot.IsReadInFlight);
        Assert.False(slot.IsOrphaned);
        Assert.False(slot.HasLimbo);
        Assert.True(slot.LimboData.IsEmpty);
        Assert.False(slot.IsDrainComplete);
        Assert.Equal(0, slot.ConsecutiveSyncReads);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_work_for_long_streamId()
    {
        var slot = new BodyDrainSlot<long>();
        var stream = new MemoryStream([]);
        slot.Initialize(99L, stream, null, CancellationToken.None, null);

        slot.Reset();

        Assert.Equal(0L, slot.StreamId);
        Assert.Null(slot.BodyStream);
    }
}
