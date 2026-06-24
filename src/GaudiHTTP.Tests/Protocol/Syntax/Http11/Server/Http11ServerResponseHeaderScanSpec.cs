using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerResponseHeaderScanSpec
{
    private static IHttpResponseFeature Feature(params (string Name, string Value)[] headers)
    {
        var feature = new TurboHttpResponseFeature { StatusCode = 200 };
        foreach (var (name, value) in headers)
        {
            feature.Headers[name] = new StringValues(value);
        }

        return feature;
    }

    [Fact(Timeout = 5000)]
    public void Scan_should_extract_content_length()
    {
        var scan = Http11ServerStateMachine.ScanResponseHeaders(Feature(("Content-Length", "42")));

        Assert.Equal(42, scan.ContentLength);
        Assert.False(scan.HasExplicitChunked);
        Assert.True(scan.EstimatedSize >= 256);
    }

    [Fact(Timeout = 5000)]
    public void Scan_should_detect_explicit_chunked()
    {
        var scan = Http11ServerStateMachine.ScanResponseHeaders(Feature(("Transfer-Encoding", "chunked")));

        Assert.Null(scan.ContentLength);
        Assert.True(scan.HasExplicitChunked);
    }

    [Fact(Timeout = 5000)]
    public void Scan_should_ignore_unparsable_content_length()
    {
        var scan = Http11ServerStateMachine.ScanResponseHeaders(Feature(("Content-Length", "notanumber")));

        Assert.Null(scan.ContentLength);
    }

    [Fact(Timeout = 5000)]
    public void Scan_should_return_floor_estimate_for_null_headers()
    {
        var scan = Http11ServerStateMachine.ScanResponseHeaders(null);

        Assert.Null(scan.ContentLength);
        Assert.False(scan.HasExplicitChunked);
        Assert.Equal(256, scan.EstimatedSize);
    }

    [Fact(Timeout = 5000)]
    public void Scan_should_grow_estimate_with_header_bytes()
    {
        var small = Http11ServerStateMachine.ScanResponseHeaders(Feature(("X-A", "1")));
        var big = Http11ServerStateMachine.ScanResponseHeaders(Feature(
            ("X-A", "1"),
            ("X-Some-Longer-Header-Name", new string('v', 400))));

        Assert.True(big.EstimatedSize > small.EstimatedSize);
    }
}
