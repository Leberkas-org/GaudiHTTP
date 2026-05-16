using Akka;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting.Logging;
using Akka.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Server.Lifecycle;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Server.Hosting;

internal sealed class TurboServerHostedService : IHostedService, IDisposable
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        """akka.loggers = ["Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting"]""");

    private readonly TurboServerOptions _options;
    private readonly TurboRouteTable _routeTable;
    private readonly TurboMiddlewareRegistry _middlewareRegistry;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;

    private ActorSystem? _system;
    private bool _ownsSystem;
    private IActorRef _supervisor = ActorRefs.Nobody;

    public TurboServerHostedService(
        TurboServerOptions options,
        TurboRouteTable routeTable,
        TurboMiddlewareRegistry middlewareRegistry,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _routeTable = routeTable;
        _middlewareRegistry = middlewareRegistry;
        _services = services;
        _loggerFactory = loggerFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _system = _services.GetService<ActorSystem>();
        if (_system is null)
        {
            var setup = BootstrapSetup.Create()
                .WithConfig(LoggingHocon)
                .And(new LoggerFactorySetup(_loggerFactory));
            _system = ActorSystem.Create("turbo-server", setup);
            _ownsSystem = true;
        }

        var materializer = _system.Materializer();
        var routeTable = _routeTable.Freeze();
        var middleware = _middlewareRegistry.Resolve(_services);

        var listenerProps = new List<Props>(_options.Endpoints.Count);
        foreach (var endpoint in _options.Endpoints)
        {
            listenerProps.Add(ListenerActor.Create(
                endpoint.Factory,
                endpoint.Options,
                _options,
                middleware,
                routeTable,
                _services,
                materializer));
        }

        _supervisor = _system.ActorOf(
            Props.Create(() => new ServerSupervisorActor()),
            "turbo-server");

        _supervisor.Tell(new ServerSupervisorActor.StartListeners(listenerProps));

        var cs = CoordinatedShutdown.Get(_system);

        cs.AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "turbo-stop-accepting", () =>
        {
            _supervisor.Tell(new ServerSupervisorActor.StopAccepting());
            return Task.FromResult(Done.Instance);
        });

        cs.AddTask(CoordinatedShutdown.PhaseServiceUnbind, "turbo-goaway", () =>
        {
            _supervisor.Tell(new ServerSupervisorActor.BeginDrain(_options.GracefulShutdownTimeout));
            return Task.FromResult(Done.Instance);
        });

        cs.AddTask(CoordinatedShutdown.PhaseServiceRequestsDone, "turbo-drain", async () =>
        {
            await Task.Delay(_options.GracefulShutdownTimeout, CancellationToken.None);
            return Done.Instance;
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_system is not null)
        {
            await CoordinatedShutdown.Get(_system).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }
    }

    public void Dispose()
    {
        if (_ownsSystem)
        {
            _system?.Dispose();
        }
    }
}
