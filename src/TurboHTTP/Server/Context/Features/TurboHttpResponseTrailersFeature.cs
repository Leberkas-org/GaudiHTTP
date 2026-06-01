using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpResponseTrailersFeature : IHttpResponseTrailersFeature
{
    private readonly TurboResponseHeaderDictionary _trailers = new();

    public IHeaderDictionary Trailers
    {
        get => _trailers;
        set { }
    }

    public IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> GetAllowedTrailers()
        => _trailers.Where(header => TrailerFieldValidator.IsAllowedInTrailer(header.Key));

    internal void Reset()
    {
        _trailers.Clear();
    }
}