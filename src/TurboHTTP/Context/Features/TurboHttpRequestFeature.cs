using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestFeature : IHttpRequestFeature
{
    public string Protocol { get; set; } = "HTTP/1.1";

    public string Scheme { get; set; } = "http";

    public string Method { get; set; } = "GET";

    public string PathBase { get; set; } = string.Empty;

    public string Path { get; set; } = "/";

    public string QueryString { get; set; } = string.Empty;

    public string RawTarget { get; set; } = "/";

    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    public Stream Body { get; set; } = Stream.Null;

    internal string? ExtractedHost { get; set; }
}
