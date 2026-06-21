using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TurboHTTP.Client;

/// <summary>
/// Extension methods for registering TurboHttp services with <see cref="IServiceCollection"/>.
/// </summary>
public static class TurboClientServiceCollectionExtensions
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        """akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]""");

    /// <summary>
    /// Registers a named TurboHttp client and returns an <see cref="ITurboHttpClientBuilder"/>
    /// for further configuration. <see cref="ITurboHttpClientFactory"/> is registered as a
    /// singleton the first time this method is called — subsequent calls are idempotent.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The logical name of the client.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this named client.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient(this IServiceCollection services,
        string name, Action<TurboClientOptions>? configure = null)
    {
        services.AddOptions();

        if (configure is not null)
        {
            services.Configure(name, configure);
        }

        services.TryAddSingleton<ITurboHttpClientFactory>(provider =>
        {
            var system = provider.GetService<ActorSystem>();
            if (system is null)
            {
                var loggerFactory = provider.GetService<ILoggerFactory>();
                if (loggerFactory is not null)
                {
                    // Bridge Akka logging to Microsoft.Extensions.Logging
                    var setup = BootstrapSetup.Create()
                        .WithConfig(LoggingHocon)
                        .And(new LoggerFactorySetup(loggerFactory));
                    system = ActorSystem.Create("turbohttp", setup);
                }
                else
                {
                    // Standalone usage — fallback to Akka's default logger
                    system = ActorSystem.Create("turbohttp", LoggingHocon);
                }

                system.Log.Info("Created ActorSystem {0}", system.Name);
            }

            var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
            var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();
            return new TurboHttpClientFactory(options, descriptors, provider, system);
        });

        // Register client name so the factory can resolve MaxEndpointSubstreams at startup.
        services.AddSingleton(new TurboHttpClientName(name));

        return new TurboHttpClientBuilder(name, services);
    }

    /// <summary>
    /// Registers the default (unnamed) TurboHttp client. Delegates to
    /// <see cref="AddTurboHttpClient(IServiceCollection, string, Action{TurboClientOptions}?)"/>
    /// with <see cref="string.Empty"/> as the name.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/>.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        => services.AddTurboHttpClient(string.Empty, configure);

    /// <summary>
    /// Registers a typed TurboHttp client where <typeparamref name="TClient"/> is both the service
    /// and implementation type. The client name is <c>typeof(TClient).Name</c>.
    /// <typeparamref name="TClient"/> is registered as a Transient service resolved via
    /// <see cref="ITurboHttpClientFactory"/>.
    /// </summary>
    /// <typeparam name="TClient">The typed client type.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this client.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient<TClient>(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        where TClient : class
    {
        var name = typeof(TClient).Name;
        services.AddTransient(sp =>
        {
            var client = sp.GetRequiredService<ITurboHttpClientFactory>().CreateClient(name);
            return ActivatorUtilities.CreateInstance<TClient>(sp, client);
        });
        return services.AddTurboHttpClient(name, configure);
    }

    /// <summary>
    /// Registers a typed TurboHttp client with a separate interface and implementation.
    /// The client name is <c>typeof(TClient).Name</c>.
    /// Both <typeparamref name="TClient"/> and <typeparamref name="TImpl"/> are registered as
    /// Transient services resolved via <see cref="ITurboHttpClientFactory"/>.
    /// </summary>
    /// <typeparam name="TClient">The service/interface type.</typeparam>
    /// <typeparam name="TImpl">The implementation type; must implement <typeparamref name="TClient"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this client.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient<TClient, TImpl>(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        where TClient : class
        where TImpl : class, TClient
    {
        var name = typeof(TClient).Name;
        services.AddTransient<TClient>(sp =>
        {
            var client = sp.GetRequiredService<ITurboHttpClientFactory>().CreateClient(name);
            return ActivatorUtilities.CreateInstance<TImpl>(sp, client);
        });
        services.AddTransient(sp =>
        {
            var client = sp.GetRequiredService<ITurboHttpClientFactory>().CreateClient(name);
            return ActivatorUtilities.CreateInstance<TImpl>(sp, client);
        });
        return services.AddTurboHttpClient(name, configure);
    }

    /// <summary>
    /// Creates the default (unnamed) <see cref="ITurboHttpClient"/> from <paramref name="factory"/>.
    /// Equivalent to calling <c>factory.CreateClient(string.Empty)</c>.
    /// </summary>
    public static ITurboHttpClient CreateClient(this ITurboHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateClient(string.Empty);
    }
}

/// <summary>
/// DI marker registered once per <c>AddTurboHttpClient()</c> call.
/// Resolved via <c>IServiceProvider.GetServices&lt;TurboHttpClientName&gt;()</c>
/// to determine the maximum <see cref="TurboClientOptions.MaxConcurrentEndpoints"/>
/// across all registered clients for dispatcher thread sizing.
/// </summary>
internal sealed record TurboHttpClientName(string Name);
