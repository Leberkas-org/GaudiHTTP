using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public static class TurboEndpointRouteBuilderExtensions
{
    public static TurboRouteHandlerBuilder MapGet(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return builder.RouteTable.Add("GET", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapGet(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return builder.RouteTable.Add("GET", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPost(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return builder.RouteTable.Add("POST", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPost(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return builder.RouteTable.Add("POST", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPut(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return builder.RouteTable.Add("PUT", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPut(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return builder.RouteTable.Add("PUT", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapDelete(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return builder.RouteTable.Add("DELETE", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapDelete(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return builder.RouteTable.Add("DELETE", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPatch(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return builder.RouteTable.Add("PATCH", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPatch(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return builder.RouteTable.Add("PATCH", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapMethods(this ITurboEndpointRouteBuilder builder, string pattern, IEnumerable<string> methods, Delegate handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = builder.RouteTable.Add(method, pattern, handler);
        }

        return last!;
    }

    public static TurboRouteHandlerBuilder MapMethods(this ITurboEndpointRouteBuilder builder, string pattern, IEnumerable<string> methods, Func<TurboHttpContext, Task> handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = builder.RouteTable.Add(method, pattern, handler);
        }

        return last!;
    }

    public static TurboRouteGroupBuilder MapGroup(this ITurboEndpointRouteBuilder builder, string prefix)
    {
        return builder.RouteTable.CreateGroup(prefix);
    }

    public static TurboRouteHandlerBuilder MapEntity(this ITurboEndpointRouteBuilder builder, string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(builder.RouteTable);
        return new TurboRouteHandlerBuilder();
    }

    public static TurboRouteHandlerBuilder MapEntity<TActorKey>(this ITurboEndpointRouteBuilder builder, string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern).UseActorRef(x => x.Get<TActorKey>());
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(builder.RouteTable);
        return new TurboRouteHandlerBuilder();
    }
}
