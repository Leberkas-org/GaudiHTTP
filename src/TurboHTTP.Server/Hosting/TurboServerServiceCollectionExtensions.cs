using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Server.Hosting;

public static class TurboServerServiceCollectionExtensions
{
    public static IServiceCollection AddTurboServer(
        this IServiceCollection services,
        Action<TurboServerOptions>? configure = null)
    {
        var options = new TurboServerOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<TurboRouteTable>();
        services.TryAddSingleton<TurboMiddlewareRegistry>();
        services.TryAddSingleton<IHostedService, TurboServerHostedService>();

        return services;
    }
}
