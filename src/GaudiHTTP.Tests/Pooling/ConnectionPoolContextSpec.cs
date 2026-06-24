using GaudiHTTP.Pooling;

namespace GaudiHTTP.Tests.Pooling;

public sealed class ConnectionPoolContextSpec
{
    private sealed class Counter : IResettable
    {
        public int Value;
        public void Reset() => Value = 0;
    }

    [Fact(Timeout = 5000)]
    public void Rent_after_return_reuses_the_same_reset_instance()
    {
        var ctx = new ConnectionPoolContext();
        var a = ctx.Rent(static () => new Counter());
        a.Value = 42;
        ctx.Return(a);

        var b = ctx.Rent(static () => new Counter());

        Assert.Same(a, b);
        Assert.Equal(0, b.Value);
    }
}
