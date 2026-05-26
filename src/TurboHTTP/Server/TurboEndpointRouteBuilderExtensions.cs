using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public static class TurboEndpointRouteBuilderExtensions
{
    private static TurboRouteTable GetTable(ITurboEndpointRouteBuilder builder)
    {
        if (builder is IRouteTableAccessor accessor)
        {
            return accessor.RouteTable;
        }

#pragma warning disable CS0618
        return builder.RouteTable;
#pragma warning restore CS0618
    }

    public static TurboRouteHandlerBuilder MapGet(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return GetTable(builder).Add("GET", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapGet(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return GetTable(builder).Add("GET", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPost(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return GetTable(builder).Add("POST", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPost(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return GetTable(builder).Add("POST", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPut(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return GetTable(builder).Add("PUT", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPut(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return GetTable(builder).Add("PUT", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapDelete(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return GetTable(builder).Add("DELETE", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapDelete(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return GetTable(builder).Add("DELETE", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPatch(this ITurboEndpointRouteBuilder builder, string pattern, Delegate handler)
    {
        return GetTable(builder).Add("PATCH", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapPatch(this ITurboEndpointRouteBuilder builder, string pattern, Func<TurboHttpContext, Task> handler)
    {
        return GetTable(builder).Add("PATCH", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapMethods(this ITurboEndpointRouteBuilder builder, string pattern, IEnumerable<string> methods, Delegate handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = GetTable(builder).Add(method, pattern, handler);
        }

        return last!;
    }

    public static TurboRouteHandlerBuilder MapMethods(this ITurboEndpointRouteBuilder builder, string pattern, IEnumerable<string> methods, Func<TurboHttpContext, Task> handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = GetTable(builder).Add(method, pattern, handler);
        }

        return last!;
    }

    public static TurboRouteGroupBuilder MapGroup(this ITurboEndpointRouteBuilder builder, string prefix)
    {
        return GetTable(builder).CreateGroup(prefix);
    }

    public static TurboRouteHandlerBuilder MapEntity(this ITurboEndpointRouteBuilder builder, string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(GetTable(builder));
        return new TurboRouteHandlerBuilder();
    }

    public static TurboRouteHandlerBuilder MapEntity<TActorKey>(this ITurboEndpointRouteBuilder builder, string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern).UseActorRef(x => x.Get<TActorKey>());
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(GetTable(builder));
        return new TurboRouteHandlerBuilder();
    }
}
