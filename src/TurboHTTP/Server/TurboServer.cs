using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting.Logging;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Server;

/// <summary>
/// TurboHTTP's ASP.NET Core <see cref="IServer"/> implementation. Manages an Akka actor system,
/// resolves configured endpoints, and routes incoming connections through the application pipeline.
/// Register via <see cref="TurboServerWebHostBuilderExtensions.UseTurboHttp"/>.
/// </summary>
public sealed class TurboServer : IServer
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        """akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]""");

    private readonly TurboServerOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;
    private readonly FeatureCollection _features = new();

    private ActorSystem? _system;
    private bool _ownsSystem;
    private IActorRef _supervisor = ActorRefs.Nobody;

    /// <summary>Initializes a new <see cref="TurboServer"/> with the provided options, logger factory, and service provider.</summary>
    public TurboServer(IOptions<TurboServerOptions> options, ILoggerFactory loggerFactory, IServiceProvider services)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _services = services;

        var addressesFeature = new ServerAddressesFeature();
        _features.Set<IServerAddressesFeature>(addressesFeature);
    }

    /// <summary>Gets the server feature collection, including the <see cref="IServerAddressesFeature"/> populated after start.</summary>
    public IFeatureCollection Features => _features;

    /// <summary>
    /// Starts the server: resolves endpoints, creates the Akka actor system if none is registered,
    /// binds listeners, and populates <see cref="Features"/> with bound addresses.
    /// </summary>
    public async Task StartAsync<TContext>(
        IHttpApplication<TContext> application,
        CancellationToken cancellationToken) where TContext : notnull
    {
        _options.Validate();

        _system = _services.GetService<ActorSystem>();
        if (_system is null)
        {
            var setup = BootstrapSetup.Create()
                .WithConfig(LoggingHocon)
                .And(new LoggerFactorySetup(_loggerFactory));
            _system = ActorSystem.Create("turbo-server", setup);
            _ownsSystem = true;
        }

        var resolver = new EndpointResolver();
        var resolvedEndpoints = resolver.Resolve(_options);

        var bridgeFlow = Flow.FromGraph(new ApplicationBridgeStage<TContext>(
            application,
            int.MaxValue,
            _options.HandlerTimeout,
            _options.HandlerGracePeriod));

        _supervisor = _system.ActorOf(
            Props.Create(() => new ServerSupervisorActor()),
            "turbo-server");

        var listenersReady = await _supervisor.Ask<ServerSupervisorActor.ListenersReady>(
            new ServerSupervisorActor.StartServer(bridgeFlow, _options, resolvedEndpoints),
            TimeSpan.FromSeconds(30),
            cancellationToken);

        var addressesFeature = _features.Get<IServerAddressesFeature>()!;
        for (var i = 0; i < resolvedEndpoints.Count; i++)
        {
            var opts = resolvedEndpoints[i].Options;
            var scheme = opts is TcpListenerOptions { ServerCertificate: not null } ? "https" : "http";
            var host = opts.Host;
            if (host is "0.0.0.0" or "::")
            {
                host = "localhost";
            }

            var port = i < listenersReady.BoundPorts.Count ? listenersReady.BoundPorts[i] : opts.Port;
            addressesFeature.Addresses.Add(string.Concat(scheme, "://", host, ":", port.ToString()));
        }

        if (_ownsSystem)
        {
            var cs = CoordinatedShutdown.Get(_system);

            cs.AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "turbo-stop-accepting", () =>
            {
                _supervisor.Tell(new ServerSupervisorActor.StopAccepting());
                return Task.FromResult(Done.Instance);
            });

            var drainTask = Task.CompletedTask;

            cs.AddTask(CoordinatedShutdown.PhaseServiceUnbind, "turbo-goaway", () =>
            {
                drainTask = _supervisor.Ask<ServerSupervisorActor.DrainComplete>(
                    new ServerSupervisorActor.BeginDrain(_options.GracefulShutdownTimeout),
                    _options.GracefulShutdownTimeout);
                return Task.FromResult(Done.Instance);
            });

            cs.AddTask(CoordinatedShutdown.PhaseServiceRequestsDone, "turbo-drain", async () =>
            {
                try
                {
                    await drainTask;
                }
                catch
                {
                    // drain may timeout if connections don't close gracefully
                }

                return Done.Instance;
            });
        }
    }

    /// <summary>
    /// Stops the server gracefully. If the server owns the actor system it runs a coordinated
    /// shutdown; otherwise it drains in-flight requests and stops the supervisor actor.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_system is null)
        {
            return;
        }

        if (_ownsSystem)
        {
            await CoordinatedShutdown.Get(_system).Run(CoordinatedShutdown.ClrExitReason.Instance);
            await _system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            _supervisor.Tell(new ServerSupervisorActor.StopAccepting());
            _supervisor.Tell(new ServerSupervisorActor.BeginDrain(_options.GracefulShutdownTimeout));
            await Task.Delay(_options.GracefulShutdownTimeout, cancellationToken);
            await _supervisor.GracefulStop(_options.GracefulShutdownTimeout);
        }
    }

    /// <summary>Disposes the actor system if this instance owns it.</summary>
    public void Dispose()
    {
        if (_ownsSystem)
        {
            _system?.Dispose();
        }
    }
}

