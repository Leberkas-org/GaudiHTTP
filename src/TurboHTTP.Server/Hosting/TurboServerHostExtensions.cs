using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Server.Hosting;

public static class TurboServerHostExtensions
{
    public static IHost UseTurboMiddleware<T>(this IHost host) where T : IServerBidiStage, new()
    {
        host.Services.GetRequiredService<TurboMiddlewareRegistry>().Add<T>();
        return host;
    }

    public static IHost UseTurboMiddleware(this IHost host, Func<IServiceProvider, IServerBidiStage> factory)
    {
        host.Services.GetRequiredService<TurboMiddlewareRegistry>().Add(factory);
        return host;
    }

    public static ITurboRouteBuilder MapTurboGet(this IHost host, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("GET", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboGet(this IHost host, string pattern, Delegate handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("GET", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboPost(this IHost host, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("POST", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboPost(this IHost host, string pattern, Delegate handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("POST", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboPut(this IHost host, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("PUT", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboPut(this IHost host, string pattern, Delegate handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("PUT", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboDelete(this IHost host, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("DELETE", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboDelete(this IHost host, string pattern, Delegate handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("DELETE", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboPatch(this IHost host, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("PATCH", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboPatch(this IHost host, string pattern, Delegate handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("PATCH", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurbo(this IHost host, string pattern, Func<TurboHttpContext, Task<HttpResponseMessage>> handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("*", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurbo(this IHost host, string pattern, Delegate handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("*", pattern, handler);
    }

    public static ITurboRouteBuilder MapTurboActor<TActor>(this IHost host, string pattern) where TActor : ActorBase
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("*", pattern,
            _ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotImplemented)));
    }

    public static ITurboRouteBuilder MapTurboStream(this IHost host, string pattern, Func<TurboHttpContext, Source<ReadOnlyMemory<byte>, NotUsed>> handler)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().Add("GET", pattern,
            _ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotImplemented)));
    }

    public static TurboRouteGroupBuilder MapTurboGroup(this IHost host, string prefix)
    {
        return host.Services.GetRequiredService<TurboRouteTable>().CreateGroup(prefix);
    }
}
