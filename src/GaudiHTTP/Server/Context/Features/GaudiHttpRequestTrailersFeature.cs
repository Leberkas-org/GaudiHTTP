using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpRequestTrailersFeature : IHttpRequestTrailersFeature
{
    private readonly GaudiHeaderDictionary _trailers = new();

    public bool Available { get; private set; }

    public IHeaderDictionary Trailers
    {
        get
        {
            if (!Available)
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

        Available = true;
    }

    internal void Reset()
    {
        Available = false;
        _trailers.Reset();
    }
}
