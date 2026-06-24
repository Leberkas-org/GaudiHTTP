using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpRequestFeature : IHttpRequestFeature
{
    private readonly GaudiHeaderDictionary _headers = new();

    public string Protocol { get; set; } = WellKnownHeaders.Http11;

    public string Scheme { get; set; } = "http";

    public string Method { get; set; } = WellKnownHeaders.Get;

    public string PathBase { get; set; } = string.Empty;

    public string Path { get; set; } = "/";

    public string QueryString { get; set; } = string.Empty;

    public string RawTarget { get; set; } = "/";

    public Stream Body { get; set; } = Stream.Null;

    public IHeaderDictionary Headers
    {
        get => _headers;
        set
        {
            _headers.Clear();
            foreach (var kvp in value)
            {
                _headers[kvp.Key] = kvp.Value;
            }
        }
    }

    internal string? ExtractedHost { get; set; }

    internal void Reset()
    {
        Protocol = WellKnownHeaders.Http11;
        Scheme = "http";
        Method = WellKnownHeaders.Get;
        PathBase = string.Empty;
        Path = "/";
        QueryString = string.Empty;
        RawTarget = "/";
        Body = Stream.Null;
        _headers.Clear();
        ExtractedHost = null;
    }
}
