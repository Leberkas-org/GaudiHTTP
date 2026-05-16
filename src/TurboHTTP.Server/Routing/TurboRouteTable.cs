using TurboHTTP.Server.Binding;

namespace TurboHTTP.Server.Routing;

public sealed class TurboRouteTable
{
    private readonly List<TurboRouteEntry> _entries = new();
    private RouteTable? _frozen;

    public ITurboRouteBuilder Add(string method, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        _entries.Add(new TurboRouteEntry(method, pattern, handler));
        return new TurboRouteBuilder();
    }

    public ITurboRouteBuilder Add(string method, string pattern, Delegate handler)
    {
        var bound = DelegateHandlerBinder.Bind(pattern, handler);
        _entries.Add(new TurboRouteEntry(method, pattern, ctx => bound(ctx, ctx.RequestServices!)));
        return new TurboRouteBuilder();
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
            builder.Add(entry.Method, entry.Pattern, entry.Handler);
        }

        _frozen = builder.Build();
        return _frozen;
    }
}
