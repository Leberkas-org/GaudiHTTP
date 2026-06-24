using GaudiHTTP.Protocol.Multiplexed;

namespace GaudiHTTP.Tests.Protocol.Multiplexed;

/// <summary>
/// Double-returning the same state to the pool must not create duplicate entries.
/// Without a guard, two Rent() calls would hand out the same object, causing two
/// H2/H3 streams to share one state — silent data cross-contamination.
/// </summary>
public sealed class StackStreamStatePoolDoubleReturnSpec
{
    private sealed class FakeState
    {
        public int Value { get; set; }
    }

    [Fact(Timeout = 5000)]
    public void Pool_should_ignore_double_return_of_same_instance()
    {
        var pool = new StackStreamStatePool<FakeState>(maxCapacity: 10, factory: () => new FakeState());

        var state = pool.Rent();
        pool.Return(state);
        pool.Return(state);

        var first = pool.Rent();
        var second = pool.Rent();

        Assert.NotSame(first, second);
    }

    [Fact(Timeout = 5000)]
    public void Pool_should_accept_return_after_re_rent()
    {
        var pool = new StackStreamStatePool<FakeState>(maxCapacity: 10, factory: () => new FakeState());

        var state = pool.Rent();
        pool.Return(state);
        var reused = pool.Rent();
        Assert.Same(state, reused);

        pool.Return(reused);
        var again = pool.Rent();
        Assert.Same(state, again);
    }
}
