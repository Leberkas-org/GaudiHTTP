using TurboHTTP.Server.Binding;

namespace TurboHTTP.Server.Routing;

public sealed class TurboRouteTable
{
    private readonly List<TurboRouteEntry> _entries = [];
    private RouteTable? _frozen;

    public TurboRouteHandlerBuilder Add(HttpMethod method, string pattern, Func<TurboHttpContext, Task> handler)
    {
        var dispatcher = new DelegateDispatcher(handler);
        _entries.Add(new TurboRouteEntry(method, pattern, dispatcher));
        return new TurboRouteHandlerBuilder();
    }

    public TurboRouteHandlerBuilder Add(HttpMethod method, string pattern, Delegate handler)
    {
        var bound = DelegateHandlerBinder.Bind(pattern, handler);
        var dispatcher = new DelegateDispatcher((ctx) => bound(ctx, ctx.RequestServices));
        _entries.Add(new TurboRouteEntry(method, pattern, dispatcher));
        return new TurboRouteHandlerBuilder();
    }

    internal TurboRouteHandlerBuilder AddWithDispatcher(HttpMethod method, string pattern, IRouteDispatcher dispatcher)
    {
        _entries.Add(new TurboRouteEntry(method, pattern, dispatcher));
        return new TurboRouteHandlerBuilder();
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

        var builder = new RouteTableBuilder();
        foreach (var entry in _entries)
        {
            builder.Add(entry.Method, entry.Pattern, entry.Dispatcher);
        }

        _frozen = builder.Build();
        return _frozen;
    }
}
