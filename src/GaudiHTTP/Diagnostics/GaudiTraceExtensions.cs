using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Servus.Diagnostics;

namespace GaudiHTTP.Diagnostics;

/// <summary>
/// Extension methods for registering GaudiTrace services with <see cref="IServiceCollection"/>.
/// </summary>
public static class GaudiTraceExtensions
{
    /// <summary>
    /// Registers a <see cref="LoggerTraceListener"/> backed by <see cref="ILoggerFactory"/> as the
    /// Servus trace sink. Calls to the internal tracing API are forwarded to the standard
    /// Microsoft.Extensions.Logging pipeline at the mapped log level.
    /// </summary>
    public static IServiceCollection AddGaudiLoggerTracing(
        this IServiceCollection services,
        TraceLevel minimumLevel = TraceLevel.Debug,
        Func<string, bool>? categoryFilter = null)
    {
        services.AddSingleton<IServusTraceListener>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var listener = new LoggerTraceListener(loggerFactory);
            Servus.Senf.Tracing.Configure(listener, minimumLevel, categoryFilter);
            return listener;
        });
        return services;
    }

    /// <summary>
    /// Registers a caller-supplied <see cref="IServusTraceListener"/> as the Servus trace sink.
    /// Use this overload when you already have a custom listener and want to configure its minimum
    /// level and optional category filter without creating a logger-backed listener.
    /// </summary>
    public static IServiceCollection AddGaudiTracing(
        this IServiceCollection services,
        IServusTraceListener listener,
        TraceLevel minimumLevel = TraceLevel.Debug,
        Func<string, bool>? categoryFilter = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        Servus.Senf.Tracing.Configure(listener, minimumLevel, categoryFilter);
        services.AddSingleton(listener);
        return services;
    }

    /// <summary>Adds the GaudiHTTP client activity source to the OpenTelemetry tracer provider.</summary>
    public static TracerProviderBuilder AddGaudiHttpInstrumentation(this TracerProviderBuilder builder)
    {
        return builder
            .AddSource(Servus.Senf.Tracing.Source.Name);
    }

    /// <summary>Adds the GaudiHTTP client meter to the OpenTelemetry meter provider.</summary>
    public static MeterProviderBuilder AddGaudiHttpInstrumentation(this MeterProviderBuilder builder)
    {
        return builder
            .AddMeter(Servus.Senf.Metrics.Meter.Name);
    }

    /// <summary>Adds the GaudiHTTP server activity source to the OpenTelemetry tracer provider.</summary>
    public static TracerProviderBuilder AddGaudiServerInstrumentation(this TracerProviderBuilder builder)
    {
        return builder
            .AddSource(Servus.Senf.Tracing.Source.Name);
    }

    /// <summary>Adds the GaudiHTTP server meter to the OpenTelemetry meter provider.</summary>
    public static MeterProviderBuilder AddGaudiServerInstrumentation(this MeterProviderBuilder builder)
    {
        return builder
            .AddMeter(Servus.Senf.Metrics.Meter.Name);
    }
}