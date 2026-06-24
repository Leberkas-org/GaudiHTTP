using GaudiHTTP.Protocol.Multiplexed;

namespace GaudiHTTP.Tests.Protocol.Multiplexed;

public sealed class StackStreamStatePoolSpec
{
    private sealed class FakeState
    {
        public int Value { get; set; }
    }

    [Fact(Timeout = 5000)]
    public void StackStreamStatePool_should_return_new_instance_when_empty()
    {
        var pool = new StackStreamStatePool<FakeState>(maxCapacity: 10, factory: () => new FakeState());
        var state = pool.Rent();
        Assert.NotNull(state);
    }

    [Fact(Timeout = 5000)]
    public void StackStreamStatePool_should_reuse_returned_instance()
    {
        var pool = new StackStreamStatePool<FakeState>(maxCapacity: 10, factory: () => new FakeState());
        var state = pool.Rent();
        state.Value = 42;
        pool.Return(state);
        var reused = pool.Rent();
        Assert.Same(state, reused);
    }

    [Fact(Timeout = 5000)]
    public void StackStreamStatePool_should_discard_when_over_capacity()
    {
        var created = 0;
        var pool = new StackStreamStatePool<FakeState>(maxCapacity: 1, factory: () =>
        {
            created++;
            return new FakeState();
        });

        var s1 = pool.Rent();
        var s2 = pool.Rent();
        pool.Return(s1);
        pool.Return(s2);

        var s3 = pool.Rent();
        Assert.Same(s1, s3);

        var s4 = pool.Rent();
        Assert.NotSame(s2, s4);
    }
}