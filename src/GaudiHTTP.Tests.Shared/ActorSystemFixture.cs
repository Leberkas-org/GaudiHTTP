using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Servus.Diagnostics;
using GaudiHTTP.Diagnostics;
using Xunit;

namespace GaudiHTTP.Tests.Shared;

public sealed class ActorSystemFixture : IAsyncLifetime
{
    private static readonly Config QuietConfig = ConfigurationFactory.ParseString("akka.loglevel = WARNING");
    private static readonly object BridgeLock = new();
    private static bool _bridgeInstalled;

    public ActorSystem System { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        // The composite root is always installed (at Trace, so additively registered diagnostic
        // sinks can capture per-packet detail); with no children it rejects every event, so CI
        // stays quiet. The console bridge floods CI output with per-request lifecycle noise
        // (pipeline materialization, teardown warnings) that CiQuietConfig cannot reach — it flows
        // through the console logger, not Akka logging — so it is only added locally, and only
        // once per process (the fixture initializes once per collection).
        Servus.Senf.Tracing.Configure(TestTracing.Root, TraceLevel.Trace);
        if (!CiQuietConfig.IsCi)
        {
            lock (BridgeLock)
            {
                if (!_bridgeInstalled)
                {
                    var loggerFactory = LoggerFactory.Create(b =>
                    {
                        b.AddConsole();
                        b.SetMinimumLevel(LogLevel.Information);
                    });

                    TestTracing.Root.Add(new LoggerTraceListener(loggerFactory), TraceLevel.Info);
                    _bridgeInstalled = true;
                }
            }
        }

        var services = new ServiceCollection();
        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create().WithConfig(CiQuietConfig.Instance.WithFallback(QuietConfig));

        var setup = bootstrap.And(diSetup);
        System = ActorSystem.Create($"GaudiHttp-v2-{Guid.NewGuid()}", setup);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await System.Terminate().WaitAsync(TimeSpan.FromSeconds(30));
        await System.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(30));
        // Deliberately no Tracing.Disable() here: fixtures are per-collection and collections run
        // in parallel, so the first collection to finish would silently kill tracing for the rest.
        // All fixtures share the same composite root; the process end tears it down.
    }
}