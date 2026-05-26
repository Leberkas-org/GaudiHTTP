using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public static class TurboMiddlewareExtensions
{
    [Obsolete("Use TurboWebApplication with Use() instead. Will be removed in 2.0.")]
    public static WebApplication UseTurbo(
        this WebApplication app,
        Func<TurboHttpContext, TurboRequestDelegate, Task> middleware)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Use(middleware);
        return app;
    }

    [Obsolete("Use TurboWebApplication with Use<T>() instead. Will be removed in 2.0.")]
    public static WebApplication UseTurbo<T>(this WebApplication app)
        where T : class, ITurboMiddleware
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Use<T>();
        return app;
    }

    [Obsolete("Use TurboWebApplication with Run() instead. Will be removed in 2.0.")]
    public static WebApplication RunTurbo(
        this WebApplication app,
        TurboRequestDelegate handler)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Run(handler);
        return app;
    }

    [Obsolete("Use TurboWebApplication with Map() instead. Will be removed in 2.0.")]
    public static WebApplication MapTurbo(
        this WebApplication app,
        string pathPrefix,
        Action<ITurboApplicationBuilder> configure)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Map(pathPrefix, configure);
        return app;
    }

    [Obsolete("Use TurboWebApplication with MapWhen() instead. Will be removed in 2.0.")]
    public static WebApplication MapTurboWhen(
        this WebApplication app,
        Func<TurboHttpContext, bool> predicate,
        Action<ITurboApplicationBuilder> configure)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .MapWhen(predicate, configure);
        return app;
    }
}