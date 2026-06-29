using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpResponseTrailersFeature : IHttpResponseTrailersFeature
{
    private readonly GaudiHeaderDictionary _trailers = new();
    // A wholesale assignment via the setter overrides the owned dictionary, mirroring Kestrel's
    // Http2Stream (_userTrailers ?? base.ResponseTrailers). The normal path (AppendTrailer) goes
    // through the getter and mutates the owned dictionary, leaving this null.
    private IHeaderDictionary? _userTrailers;

    public IHeaderDictionary Trailers
    {
        get => _userTrailers ?? _trailers;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _userTrailers = value;
        }
    }

    public IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> GetAllowedTrailers()
        => Trailers.Where(header => TrailerFieldValidator.IsAllowedInTrailer(header.Key));

    internal void Reset()
    {
        _trailers.Clear();
        _userTrailers = null;
    }
}