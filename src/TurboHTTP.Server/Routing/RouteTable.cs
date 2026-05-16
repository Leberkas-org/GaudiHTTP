using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.Server.Routing;

public sealed class RouteMatchResult
{
    public static readonly RouteMatchResult NoMatch = new(false, null, new RouteValueDictionary());

    public bool IsMatch { get; }
    public Func<TurboHttpContext, Task<HttpResponseMessage>>? Handler { get; }
    public RouteValueDictionary RouteValues { get; }

    internal RouteMatchResult(bool isMatch, Func<TurboHttpContext, Task<HttpResponseMessage>>? handler, RouteValueDictionary routeValues)
    {
        IsMatch = isMatch;
        Handler = handler;
        RouteValues = routeValues;
    }
}

public sealed class RouteTable
{
    private readonly RouteEntry[] _entries;

    internal RouteTable(RouteEntry[] entries)
    {
        _entries = entries;
    }

    public RouteMatchResult Match(string method, string path)
    {
        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in _entries)
        {
            if (entry.IsStaticMatch(pathSegments)
                && (entry.Method == "*" || string.Equals(entry.Method, method, StringComparison.OrdinalIgnoreCase)))
            {
                return new RouteMatchResult(true, entry.Handler, new RouteValueDictionary());
            }
        }

        foreach (var entry in _entries)
        {
            if (entry.IsStaticMatch(pathSegments))
            {
                continue;
            }

            var routeValues = new RouteValueDictionary();
            if (entry.TryMatch(method, path, routeValues))
            {
                return new RouteMatchResult(true, entry.Handler, routeValues);
            }
        }

        return RouteMatchResult.NoMatch;
    }
}

public sealed class RouteTableBuilder
{
    private readonly List<RouteEntry> _entries = [];

    public RouteTableBuilder Add(string method, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        _entries.Add(new RouteEntry(method, pattern, handler));
        return this;
    }

    public RouteTable Build()
    {
        return new RouteTable(_entries.ToArray());
    }
}
