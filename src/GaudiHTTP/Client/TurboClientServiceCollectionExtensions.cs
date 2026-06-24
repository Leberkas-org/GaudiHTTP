using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaudiHTTP.Client;

/// <summary>
/// Extension methods for registering GaudiHttp services with <see cref="IServiceCollection"/>.
/// </summary>
public static class TurboClientServiceCollectionExtensions
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        """akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]""");

    /// <summary>
    /// Registers a named GaudiHttp client and returns an <see cref="IGaudiHttpClientBuilder"/>
    /// for further configuration. <see cref="IGaudiHttpClientFactory"/> is registered as a
    /// singleton the first time this method is called — subsequent calls are idempotent.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The logical name of the client.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this named client.</param>
    /// <returns>An <see cref="IGaudiHttpClientBuilder"/> for further configuration.</returns>
    public static IGaudiHttpClientBuilder AddGaudiHttpClient(this IServiceCollection services,
        string name, Action<TurboClientOptions>? configure = null)
    {
        services.AddOptions();

        if (configure is not null)
        {
            services.Configure(name, configure);
        }

        services.TryAddSingleton<IGaudiHttpClientFactory>(provider =>
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
                    system = ActorSystem.Create("GaudiHttp", setup);
                }
                else
                {
                    // Standalone usage — fallback to Akka's default logger
                    system = ActorSystem.Create("GaudiHttp", LoggingHocon);
                }

                system.Log.Info("Created ActorSystem {0}", system.Name);
            }

            var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
            var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();
            return new GaudiHttpClientFactory(options, descriptors, provider, system);
        });

        // Register client name so the factory can resolve MaxEndpointSubstreams at startup.
        services.AddSingleton(new GaudiHttpClientName(name));

        return new GaudiHttpClientBuilder(name, services);
    }

    /// <summary>
    /// Registers the default (unnamed) GaudiHttp client. Delegates to
    /// <see cref="AddGaudiHttpClient(IServiceCollection, string, Action{TurboClientOptions}?)"/>
    /// with <see cref="string.Empty"/> as the name.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/>.</param>
    /// <returns>An <see cref="IGaudiHttpClientBuilder"/> for further configuration.</returns>
    public static IGaudiHttpClientBuilder AddGaudiHttpClient(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        => services.AddGaudiHttpClient(string.Empty, configure);

    /// <summary>
    /// Registers a typed GaudiHttp client where <typeparamref name="TClient"/> is both the service
    /// and implementation type. The client name is <c>typeof(TClient).Name</c>.
    /// <typeparamref name="TClient"/> is registered as a Transient service resolved via
    /// <see cref="IGaudiHttpClientFactory"/>.
    /// </summary>
    /// <typeparam name="TClient">The typed client type.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this client.</param>
    /// <returns>An <see cref="IGaudiHttpClientBuilder"/> for further configuration.</returns>
    public static IGaudiHttpClientBuilder AddGaudiHttpClient<TClient>(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        where TClient : class
    {
        var name = typeof(TClient).Name;
        services.AddTransient(sp =>
        {
            var client = sp.GetRequiredService<IGaudiHttpClientFactory>().CreateClient(name);
            return ActivatorUtilities.CreateInstance<TClient>(sp, client);
        });
        return services.AddGaudiHttpClient(name, configure);
    }

    /// <summary>
    /// Registers a typed GaudiHttp client with a separate interface and implementation.
    /// The client name is <c>typeof(TClient).Name</c>.
    /// Both <typeparamref name="TClient"/> and <typeparamref name="TImpl"/> are registered as
    /// Transient services resolved via <see cref="IGaudiHttpClientFactory"/>.
    /// </summary>
    /// <typeparam name="TClient">The service/interface type.</typeparam>
    /// <typeparam name="TImpl">The implementation type; must implement <typeparamref name="TClient"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this client.</param>
    /// <returns>An <see cref="IGaudiHttpClientBuilder"/> for further configuration.</returns>
    public static IGaudiHttpClientBuilder AddGaudiHttpClient<TClient, TImpl>(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        where TClient : class
        where TImpl : class, TClient
    {
        var name = typeof(TClient).Name;
        services.AddTransient<TClient>(sp =>
        {
            var client = sp.GetRequiredService<IGaudiHttpClientFactory>().CreateClient(name);
            return ActivatorUtilities.CreateInstance<TImpl>(sp, client);
        });
        services.AddTransient(sp =>
        {
            var client = sp.GetRequiredService<IGaudiHttpClientFactory>().CreateClient(name);
            return ActivatorUtilities.CreateInstance<TImpl>(sp, client);
        });
        return services.AddGaudiHttpClient(name, configure);
    }

    /// <summary>
    /// Creates the default (unnamed) <see cref="IGaudiHttpClient"/> from <paramref name="factory"/>.
    /// Equivalent to calling <c>factory.CreateClient(string.Empty)</c>.
    /// </summary>
    public static IGaudiHttpClient CreateClient(this IGaudiHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateClient(string.Empty);
    }
}

/// <summary>
/// DI marker registered once per <c>AddGaudiHttpClient()</c> call.
/// Resolved via <c>IServiceProvider.GetServices&lt;GaudiHttpClientName&gt;()</c>
/// to determine the maximum <see cref="TurboClientOptions.MaxConcurrentEndpoints"/>
/// across all registered clients for dispatcher thread sizing.
/// </summary>
internal sealed record GaudiHttpClientName(string Name);
