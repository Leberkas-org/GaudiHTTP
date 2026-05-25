using Microsoft.Extensions.Hosting;
using TurboHTTP.Routing;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public sealed class TurboWebApplication : IHost, IAsyncDisposable
{
    private readonly IHost _host;
    private readonly TurboRouteTable _routeTable;
    private readonly TurboPipelineBuilder _pipelineBuilder;

    internal TurboWebApplication(IHost host, TurboRouteTable routeTable, TurboPipelineBuilder pipelineBuilder)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _routeTable = routeTable ?? throw new ArgumentNullException(nameof(routeTable));
        _pipelineBuilder = pipelineBuilder ?? throw new ArgumentNullException(nameof(pipelineBuilder));
    }

    public IServiceProvider Services => _host.Services;

    public void Dispose()
    {
        _host.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _host.Dispose();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _host.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _host.StopAsync(cancellationToken);
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return _host.RunAsync(cancellationToken);
    }

    public async Task RunAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await _host.RunAsync(cts.Token);
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
    {
        return _host.WaitForShutdownAsync(cancellationToken);
    }

    public TurboWebApplication MapGet(string pattern, Func<TurboHttpContext, Task> handler)
    {
        _routeTable.Add("GET", pattern, handler);
        return this;
    }

    public TurboWebApplication MapGet(string pattern, Delegate handler)
    {
        _routeTable.Add("GET", pattern, handler);
        return this;
    }

    public TurboWebApplication MapPost(string pattern, Func<TurboHttpContext, Task> handler)
    {
        _routeTable.Add("POST", pattern, handler);
        return this;
    }

    public TurboWebApplication MapPost(string pattern, Delegate handler)
    {
        _routeTable.Add("POST", pattern, handler);
        return this;
    }

    public TurboWebApplication MapPut(string pattern, Func<TurboHttpContext, Task> handler)
    {
        _routeTable.Add("PUT", pattern, handler);
        return this;
    }

    public TurboWebApplication MapPut(string pattern, Delegate handler)
    {
        _routeTable.Add("PUT", pattern, handler);
        return this;
    }

    public TurboWebApplication MapDelete(string pattern, Func<TurboHttpContext, Task> handler)
    {
        _routeTable.Add("DELETE", pattern, handler);
        return this;
    }

    public TurboWebApplication MapDelete(string pattern, Delegate handler)
    {
        _routeTable.Add("DELETE", pattern, handler);
        return this;
    }

    public TurboWebApplication MapPatch(string pattern, Func<TurboHttpContext, Task> handler)
    {
        _routeTable.Add("PATCH", pattern, handler);
        return this;
    }

    public TurboWebApplication MapPatch(string pattern, Delegate handler)
    {
        _routeTable.Add("PATCH", pattern, handler);
        return this;
    }

    public TurboWebApplication MapMethods(string pattern, IEnumerable<string> methods,
        Func<TurboHttpContext, Task> handler)
    {
        foreach (var method in methods)
        {
            _routeTable.Add(method, pattern, handler);
        }

        return this;
    }

    public TurboWebApplication MapMethods(string pattern, IEnumerable<string> methods, Delegate handler)
    {
        foreach (var method in methods)
        {
            _routeTable.Add(method, pattern, handler);
        }

        return this;
    }

    public TurboRouteGroupBuilder MapGroup(string prefix)
    {
        return _routeTable.CreateGroup(prefix);
    }

    public TurboWebApplication MapEntity(string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(_routeTable);
        return this;
    }

    public TurboWebApplication MapEntity<TActorKey>(string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern).UseActorRef(x => x.Get<TActorKey>());
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(_routeTable);
        return this;
    }

    public TurboWebApplication Use(Func<TurboHttpContext, TurboRequestDelegate, Task> middleware)
    {
        _pipelineBuilder.Use(middleware);
        return this;
    }

    public TurboWebApplication Use<T>() where T : class, ITurboMiddleware
    {
        _pipelineBuilder.Use<T>();
        return this;
    }

    public TurboWebApplication Run(TurboRequestDelegate handler)
    {
        _pipelineBuilder.Run(handler);
        return this;
    }

    public TurboWebApplication Map(string pathPrefix, Action<ITurboPipelineBuilder> configure)
    {
        _pipelineBuilder.Map(pathPrefix, configure);
        return this;
    }

    public TurboWebApplication MapWhen(Func<TurboHttpContext, bool> predicate, Action<ITurboPipelineBuilder> configure)
    {
        _pipelineBuilder.MapWhen(predicate, configure);
        return this;
    }
}