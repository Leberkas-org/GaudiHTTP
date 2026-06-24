using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GaudiHTTP.Server;

/// <summary>Extension methods for <see cref="IHostBuilder"/> to register GaudiHTTP as the ASP.NET Core server.</summary>
public static class TurboServerWebHostBuilderExtensions
{
    /// <summary>
    /// Replaces the registered <see cref="IServer"/> with <see cref="TurboServer"/> and optionally
    /// applies <paramref name="configure"/> to <see cref="TurboServerOptions"/>.
    /// </summary>
    public static IHostBuilder UseGaudiHttp(
        this IHostBuilder builder,
        Action<TurboServerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServer>();
            services.AddSingleton<IServer, TurboServer>();
            if (configure is not null)
            {
                services.Configure(configure);
            }
        });
        return builder;
    }
}
