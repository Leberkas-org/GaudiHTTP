using System.Net;
using GaudiHTTP.Client;
using GaudiHTTP.Streams.Stages.Client;

namespace GaudiHTTP.Tests.Streams.Stages.Client;

public sealed class RequestEnricherProxySpec
{
    private static TurboRequestOptions Options(bool useProxy = true, IWebProxy? proxy = null)
    {
        return new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            UseProxy: useProxy,
            Proxy: proxy);
    }

    private static HttpRequestMessage Http3Request(HttpVersionPolicy policy = HttpVersionPolicy.RequestVersionOrLower)
    {
        return new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource")
        {
            Version = HttpVersion.Version30,
            VersionPolicy = policy
        };
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_downgrade_http3_to_http2_when_proxy_applies()
    {
        var enricher = new RequestEnricher(() => Options(proxy: new WebProxy("http://proxy.local:8080")));

        var result = enricher.Enrich(Http3Request());

        Assert.Equal(HttpVersion.Version20, result.Version);
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_throw_for_http3_exact_when_proxy_applies()
    {
        var enricher = new RequestEnricher(() => Options(proxy: new WebProxy("http://proxy.local:8080")));

        Assert.Throws<HttpRequestException>(
            () => enricher.Enrich(Http3Request(HttpVersionPolicy.RequestVersionExact)));
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_throw_for_http3_or_higher_when_proxy_applies()
    {
        var enricher = new RequestEnricher(() => Options(proxy: new WebProxy("http://proxy.local:8080")));

        Assert.Throws<HttpRequestException>(
            () => enricher.Enrich(Http3Request(HttpVersionPolicy.RequestVersionOrHigher)));
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_keep_http3_when_no_proxy_configured()
    {
        var enricher = new RequestEnricher(() => Options(proxy: null));

        var result = enricher.Enrich(Http3Request());

        Assert.Equal(HttpVersion.Version30, result.Version);
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_keep_http3_when_use_proxy_is_false()
    {
        var enricher = new RequestEnricher(
            () => Options(useProxy: false, proxy: new WebProxy("http://proxy.local:8080")));

        var result = enricher.Enrich(Http3Request());

        Assert.Equal(HttpVersion.Version30, result.Version);
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_keep_http3_when_proxy_bypasses_host()
    {
        var proxy = new WebProxy("http://proxy.local:8080")
        {
            BypassList = [@"example\.com"]
        };
        var enricher = new RequestEnricher(() => Options(proxy: proxy));

        var result = enricher.Enrich(Http3Request());

        Assert.Equal(HttpVersion.Version30, result.Version);
    }

    [Fact(Timeout = 5000)]
    public void Enrich_should_not_touch_http2_when_proxy_applies()
    {
        var enricher = new RequestEnricher(() => Options(proxy: new WebProxy("http://proxy.local:8080")));
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource")
        {
            Version = HttpVersion.Version20
        };

        var result = enricher.Enrich(request);

        Assert.Equal(HttpVersion.Version20, result.Version);
    }
}
