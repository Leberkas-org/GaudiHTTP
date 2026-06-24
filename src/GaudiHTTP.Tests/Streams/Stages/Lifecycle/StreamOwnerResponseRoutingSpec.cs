using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;

/// <summary>
/// A response must route to the PartitionHub slot of the consumer that issued it. The slot is the
/// 0-based position of the consumer in the attached-consumer order. Previously partitions were a
/// 1-based monotonic counter that did not compact on unregister, so responses were misrouted to
/// the wrong consumer when two clients shared a StreamOwner (same client name).
/// </summary>
public sealed class StreamOwnerResponseRoutingSpec
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact(Timeout = 5000)]
    public void First_consumer_should_map_to_partition_zero()
    {
        Assert.Equal(0, StreamOwner.ResolvePartitionIndex([A, B], A, consumerCount: 2));
    }

    [Fact(Timeout = 5000)]
    public void Second_consumer_should_map_to_partition_one()
    {
        Assert.Equal(1, StreamOwner.ResolvePartitionIndex([A, B], B, consumerCount: 2));
    }

    [Fact(Timeout = 5000)]
    public void Third_consumer_should_map_to_its_index()
    {
        Assert.Equal(2, StreamOwner.ResolvePartitionIndex([A, B, C], C, consumerCount: 3));
    }

    [Fact(Timeout = 5000)]
    public void Index_should_compact_after_an_earlier_consumer_unregisters()
    {
        // A unregistered: B is now the only consumer and occupies the hub's slot 0.
        Assert.Equal(0, StreamOwner.ResolvePartitionIndex([B], B, consumerCount: 1));
    }

    [Fact(Timeout = 5000)]
    public void Unknown_consumer_should_fall_back_to_partition_zero()
    {
        Assert.Equal(0, StreamOwner.ResolvePartitionIndex([A, B], C, consumerCount: 2));
    }

    [Fact(Timeout = 5000)]
    public void Index_beyond_consumer_count_should_fall_back_to_partition_zero()
    {
        // Defensive: a stale index outside the live partition set must not route out of bounds.
        Assert.Equal(0, StreamOwner.ResolvePartitionIndex([A, B, C], C, consumerCount: 2));
    }
}
