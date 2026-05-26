using TurboHTTP.Server;
using TurboHTTP.Routing.Binding;

namespace TurboHTTP.Routing;

public sealed class TurboRouteTable
{
    private readonly List<(RouteEntry Entry, TurboRouteHandlerBuilder Builder)> _entries = [];
    private RouteTable? _frozen;

    public TurboRouteHandlerBuilder Add(string method, string pattern, Func<TurboHttpContext, Task> handler)
    {
        var dispatcher = new DelegateDispatcher(handler);
        var builder = new TurboRouteHandlerBuilder();
        _entries.Add((new RouteEntry(method, pattern, dispatcher), builder));
        return builder;
    }

    public TurboRouteHandlerBuilder Add(string method, string pattern, Delegate handler)
    {
        var bound = DelegateHandlerBinder.Bind(pattern, handler);
        var dispatcher = new DelegateDispatcher((ctx) => bound(ctx, ctx.RequestServices));
        var builder = new TurboRouteHandlerBuilder();
        _entries.Add((new RouteEntry(method, pattern, dispatcher), builder));
        return builder;
    }

    internal TurboRouteHandlerBuilder AddWithDispatcher(string method, string pattern, IRouteDispatcher dispatcher)
    {
        var builder = new TurboRouteHandlerBuilder();
        _entries.Add((new RouteEntry(method, pattern, dispatcher), builder));
        return builder;
    }

    public TurboRouteGroupBuilder CreateGroup(string prefix)
    {
        return new TurboRouteGroupBuilder(prefix, this);
    }

    internal RouteTable Freeze()
    {
        if (_frozen is not null)
        {
            return _frozen;
        }

        var entriesWithMetadata = new RouteEntry[_entries.Count];
        for (var i = 0; i < _entries.Count; i++)
        {
            var (entry, builder) = _entries[i];
            var metadata = builder.BuildMetadata();
            entriesWithMetadata[i] = new RouteEntry(entry.Method, entry.Pattern, entry.Dispatcher, metadata);
        }

        _frozen = new RouteTable(entriesWithMetadata);
        return _frozen;
    }
}
