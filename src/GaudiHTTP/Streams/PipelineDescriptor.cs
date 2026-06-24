using GaudiHTTP.Client;
using GaudiHTTP.Features.AltSvc;
using GaudiHTTP.Features.Caching;
using GaudiHTTP.Features.Cookies;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Streams;

internal sealed record PipelineDescriptor(
    RedirectPolicy? RedirectPolicy,
    RetryPolicy? RetryPolicy,
    Expect100Policy? Expect100Policy,
    CompressionPolicy? CompressionPolicy,
    CookieJar? CookieJar,
    Cache? CacheStore,
    CachePolicy? CachePolicy,
    IReadOnlyList<GaudiHandler> Handlers,
    bool AutomaticDecompression = true,
    AltSvcCache? AltSvcCache = null,
    bool UseProxy = true,
    System.Net.IWebProxy? Proxy = null)
{
    public static readonly PipelineDescriptor Empty = new(
        RedirectPolicy: null,
        RetryPolicy: null,
        Expect100Policy: null,
        CompressionPolicy: null,
        CookieJar: null,
        CacheStore: null,
        CachePolicy: null,
        Handlers: [],
        AutomaticDecompression: true,
        AltSvcCache: null);
}