using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class DynamicHubSpec : StreamTestBase
{
    private static DynamicHub<int, int> Hub(int bufferSize = 256, int perConsumerBufferSize = 16)
        => new(x => x % 10, bufferSize, perConsumerBufferSize);

    [Fact(Timeout = 5000)]
    public void DynamicHub_should_route_element_to_matching_key_source_only()
    {
        var (up, hub) = this.SourceProbe<int>()
            .ToMaterialized(Hub(), Keep.Both)
            .Run(Materializer);

        var down1 = hub.Source(1).RunWith(this.SinkProbe<int>(), Materializer);
        var down2 = hub.Source(2).RunWith(this.SinkProbe<int>(), Materializer);

        down1.Request(10);
        down2.Request(10);

        up.SendNext(11, TestContext.Current.CancellationToken); // key 1
        up.SendNext(22, TestContext.Current.CancellationToken); // key 2
        up.SendNext(31, TestContext.Current.CancellationToken); // key 1

        Assert.Equal(11, down1.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.Equal(31, down1.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.Equal(22, down2.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        down2.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }
}
