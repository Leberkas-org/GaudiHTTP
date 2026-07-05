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

    public ActorSystem System { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        // Senf tracing at Info floods CI output with per-request lifecycle noise (pipeline
        // materialization, teardown warnings) that CiQuietConfig cannot reach — it flows through
        // the console logger, not Akka logging. Skip tracing entirely in CI; full Info locally.
        if (!CiQuietConfig.IsCi)
        {
            var loggerFactory = LoggerFactory.Create(b =>
            {
                b.AddConsole();
                b.SetMinimumLevel(LogLevel.Information);
            });

            var traceListener = new LoggerTraceListener(loggerFactory);
            Servus.Senf.Tracing.Configure(traceListener, TraceLevel.Info);
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
        Servus.Senf.Tracing.Disable();
    }
}