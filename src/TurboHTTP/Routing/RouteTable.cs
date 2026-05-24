using Microsoft.AspNetCore.Routing;

namespace TurboHTTP.Routing;

public sealed class RouteMatchResult
{
    internal static readonly RouteValueDictionary EmptyRouteValues = new();
    public static readonly RouteMatchResult NoMatch = new(false, null, EmptyRouteValues);

    public bool IsMatch { get; }
    internal IRouteDispatcher? Dispatcher { get; }
    public RouteValueDictionary RouteValues { get; }

    internal RouteMatchResult(bool isMatch, IRouteDispatcher? dispatcher, RouteValueDictionary routeValues)
    {
        IsMatch = isMatch;
        Dispatcher = dispatcher;
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

    public RouteMatchResult Match(HttpMethod method, string path)
    {
        foreach (var entry in _entries)
        {
            if (entry.IsStaticMatch(path)
                && (entry.Method.Method == "*" || entry.Method.Equals(method)))
            {
                return new RouteMatchResult(true, entry.Dispatcher, RouteMatchResult.EmptyRouteValues);
            }
        }

        foreach (var entry in _entries)
        {
            if (entry.IsStaticMatch(path))
            {
                continue;
            }

            var routeValues = new RouteValueDictionary();
            if (entry.TryMatch(method, path, routeValues))
            {
                return new RouteMatchResult(true, entry.Dispatcher, routeValues);
            }
        }

        return RouteMatchResult.NoMatch;
    }
}

internal sealed class RouteTableBuilder
{
    private readonly List<RouteEntry> _entries = [];

    public RouteTableBuilder Add(HttpMethod method, string pattern, IRouteDispatcher dispatcher)
    {
        _entries.Add(new RouteEntry(method, pattern, dispatcher));
        return this;
    }

    public RouteTable Build()
    {
        return new RouteTable(_entries.ToArray());
    }
}
