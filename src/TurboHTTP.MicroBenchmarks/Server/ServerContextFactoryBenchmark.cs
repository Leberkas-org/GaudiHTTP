using System.Net;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Context.Features;
using TurboHTTP.MicroBenchmarks.Internal;
using TurboHTTP.Server;

namespace TurboHTTP.MicroBenchmarks.Server;

[Config(typeof(MicroBenchmarkConfig))]
public sealed class ServerContextFactoryBenchmark
{
    private TurboHttpRequestFeature _requestFeature = null!;
    private TurboConnectionInfo _connectionInfo = null!;
    private IServiceProvider _serviceProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        _requestFeature = new TurboHttpRequestFeature
        {
            Method = "GET",
            Path = "/api/test",
            Protocol = "HTTP/1.1",
            Scheme = "http",
            Headers = new HeaderDictionary(),
            Body = Stream.Null
        };

        _connectionInfo = new TurboConnectionInfo(
            id: Guid.NewGuid().ToString("N"),
            remoteIpAddress: IPAddress.Parse("127.0.0.1"),
            remotePort: 12345,
            localIpAddress: IPAddress.Parse("127.0.0.1"),
            localPort: 80);

        // Create a minimal service provider
        _serviceProvider = new MinimalServiceProvider();
    }

    [Benchmark(Baseline = true)]
    public TurboHttpContext Create_WithPooling()
    {
        return ServerContextFactory.Create(
            _requestFeature,
            hasBody: false,
            services: _serviceProvider,
            connectionInfo: _connectionInfo);
    }

    [Benchmark]
    public void Create_Return_Cycle()
    {
        var context = ServerContextFactory.Create(
            _requestFeature,
            hasBody: false,
            services: _serviceProvider,
            connectionInfo: _connectionInfo);

        ServerContextFactory.Return(context);
    }

    [Benchmark]
    public TurboHttpContext Create_WithoutPooling()
    {
        return ServerContextFactory.Create(
            _requestFeature,
            hasBody: false,
            services: null,
            connectionInfo: null);
    }

    /// <summary>
    /// Minimal service provider implementation for benchmarking.
    /// </summary>
    private sealed class MinimalServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
