using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestTrailersFeature : IHttpRequestTrailersFeature
{
    private readonly TurboResponseHeaderDictionary _trailers = new();
    private bool _available;

    public bool Available => _available;

    public IHeaderDictionary Trailers
    {
        get
        {
            if (!_available)
            {
                throw new InvalidOperationException(
                    "Request trailers are not yet available. The request body must be fully consumed first.");
            }

            return _trailers;
        }
    }

    public void SetAvailable(IReadOnlyList<(string Name, string Value)> trailers)
    {
        _trailers.Clear();

        foreach (var (name, value) in trailers)
        {
            if (TrailerFieldValidator.IsAllowedInTrailer(name))
            {
                _trailers.Add(name, value);
            }
        }

        _available = true;
    }

    internal void Reset()
    {
        _available = false;
        _trailers.Reset();
    }
}
