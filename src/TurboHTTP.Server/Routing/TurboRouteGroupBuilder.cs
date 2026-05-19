using TurboHTTP.Server.Entity.Builder;

namespace TurboHTTP.Server.Routing;

public sealed class TurboRouteGroupBuilder
{
    private readonly string _prefix;
    private readonly TurboRouteTable _table;
    private readonly EndpointMetadata _groupMetadata = new();

    internal TurboRouteGroupBuilder(string prefix, TurboRouteTable table)
    {
        _prefix = prefix;
        _table = table;
    }

    public TurboRouteHandlerBuilder MapGet(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Get, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapPost(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Post, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapPut(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Put, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapDelete(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Delete, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapPatch(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Patch, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapMethods(string pattern, IEnumerable<HttpMethod> methods, Delegate handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = _table.Add(method, _prefix + pattern, handler);
        }

        return last!;
    }

    public TurboRouteGroupBuilder MapGroup(string prefix)
    {
        return new TurboRouteGroupBuilder(_prefix + prefix, _table);
    }

    public TurboRouteGroupBuilder WithTags(params string[] tags)
    {
        _groupMetadata.Tags.AddRange(tags);
        return this;
    }

    public TurboRouteGroupBuilder WithMetadata(params object[] metadata)
    {
        _groupMetadata.Items.AddRange(metadata);
        return this;
    }

    public TurboRouteGroupBuilder RequireAuthorization()
    {
        _groupMetadata.RequiresAuthorization = true;
        return this;
    }

    public TurboRouteGroupBuilder AllowAnonymous()
    {
        _groupMetadata.AllowsAnonymous = true;
        return this;
    }

    public TurboRouteHandlerBuilder MapEntity<TKey>(
        string pattern, Action<TurboEntityBuilder<TKey>> configure)
    {
        var entityBuilder = new TurboEntityBuilder<TKey>(_prefix + pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(_table);
        return new TurboRouteHandlerBuilder();
    }
}