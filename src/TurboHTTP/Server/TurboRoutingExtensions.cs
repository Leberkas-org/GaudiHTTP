using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public static class TurboRoutingExtensions
{
    [Obsolete("Use TurboWebApplication with MapGet() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboGet(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("GET", pattern, handler);
    }

    [Obsolete("Use TurboWebApplication with MapPost() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboPost(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("POST", pattern, handler);
    }

    [Obsolete("Use TurboWebApplication with MapPut() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboPut(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("PUT", pattern, handler);
    }

    [Obsolete("Use TurboWebApplication with MapDelete() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboDelete(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("DELETE", pattern, handler);
    }

    [Obsolete("Use TurboWebApplication with MapPatch() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboPatch(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("PATCH", pattern, handler);
    }

    [Obsolete("Use TurboWebApplication with MapMethods() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboMethods(
        this WebApplication app, string pattern, IEnumerable<string> methods, Delegate handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = app.Services.GetRequiredService<TurboRouteTable>().Add(method, pattern, handler);
        }

        return last!;
    }

    [Obsolete("Use TurboWebApplication with MapGroup() instead. Will be removed in 2.0.")]
    public static TurboRouteGroupBuilder MapTurboGroup(this WebApplication app, string prefix)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().CreateGroup(prefix);
    }

    [Obsolete("Use TurboWebApplication with MapEntity() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboEntity(this WebApplication app, string pattern,
        Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(app.Services.GetRequiredService<TurboRouteTable>());
        return new TurboRouteHandlerBuilder();
    }

    [Obsolete("Use TurboWebApplication with MapEntity() instead. Will be removed in 2.0.")]
    public static TurboRouteHandlerBuilder MapTurboEntity<TActorKey>(this WebApplication app, string pattern,
        Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern).UseActorRef(x => x.Get<TActorKey>());
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(app.Services.GetRequiredService<TurboRouteTable>());
        return new TurboRouteHandlerBuilder();
    }

}