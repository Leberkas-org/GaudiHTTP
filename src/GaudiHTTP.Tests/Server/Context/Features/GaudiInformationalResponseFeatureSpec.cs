using Microsoft.AspNetCore.Http;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Tests.Server.Context.Features;

public sealed class GaudiInformationalResponseFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void SendInformational_should_invoke_callback_with_status_and_headers()
    {
        int receivedStatus = 0;
        IHeaderDictionary? receivedHeaders = null;
        var feature = new GaudiInformationalResponseFeature((status, headers) =>
        {
            receivedStatus = status;
            receivedHeaders = headers;
        });

        var headers = new HeaderDictionary { ["Link"] = "</style.css>; rel=preload" };
        feature.SendInformational(103, headers);

        Assert.Equal(103, receivedStatus);
        Assert.Same(headers, receivedHeaders);
    }

    [Fact(Timeout = 5000)]
    public void SendInformational_should_accept_100_continue()
    {
        var called = false;
        var feature = new GaudiInformationalResponseFeature((_, _) => called = true);

        feature.SendInformational(100, new HeaderDictionary());

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public void SendInformational_should_reject_status_below_100()
    {
        var feature = new GaudiInformationalResponseFeature((_, _) => { });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            feature.SendInformational(99, new HeaderDictionary()));
    }

    [Fact(Timeout = 5000)]
    public void SendInformational_should_reject_status_200_or_above()
    {
        var feature = new GaudiInformationalResponseFeature((_, _) => { });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            feature.SendInformational(200, new HeaderDictionary()));
    }

    [Fact(Timeout = 5000)]
    public void SendInformational_should_reject_after_final_response()
    {
        var feature = new GaudiInformationalResponseFeature((_, _) => { });
        feature.MarkFinalResponseSent();

        Assert.Throws<InvalidOperationException>(() =>
            feature.SendInformational(100, new HeaderDictionary()));
    }

    [Fact(Timeout = 5000)]
    public void SendInformational_should_allow_multiple_1xx_before_final()
    {
        var count = 0;
        var feature = new GaudiInformationalResponseFeature((_, _) => count++);

        feature.SendInformational(100, new HeaderDictionary());
        feature.SendInformational(103, new HeaderDictionary());

        Assert.Equal(2, count);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_final_response_flag()
    {
        var called = false;
        var feature = new GaudiInformationalResponseFeature((_, _) => called = true);
        feature.MarkFinalResponseSent();
        feature.Reset();

        feature.SendInformational(100, new HeaderDictionary());

        Assert.True(called);
    }
}
