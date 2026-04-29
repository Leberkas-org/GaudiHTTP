using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport;

public sealed class ConnectionReuseSpec
{
    [Fact(Timeout = 5000)]
    public void ConnectionReuse_should_implement_ITransportOutbound()
    {
        var msg = new ConnectionReuse(PoolAction.Reuse);
        Assert.IsAssignableFrom<ITransportOutbound>(msg);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionReuse_should_store_action()
    {
        var reuse = new ConnectionReuse(PoolAction.Reuse);
        Assert.Equal(PoolAction.Reuse, reuse.Action);

        var dispose = new ConnectionReuse(PoolAction.Dispose);
        Assert.Equal(PoolAction.Dispose, dispose.Action);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionReuse_should_have_value_equality()
    {
        var a = new ConnectionReuse(PoolAction.Reuse);
        var b = new ConnectionReuse(PoolAction.Reuse);
        Assert.Equal(a, b);
    }
}
