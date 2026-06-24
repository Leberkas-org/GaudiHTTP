using System.Text;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Streams;
using Xunit;

namespace GaudiHTTP.Tests.Shared;

public abstract class AcceptanceTestBase : EngineTestBase
{
    internal static IClientProtocolEngine CreateHttp10Engine(Action<Http1ClientOptions>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http1);
        return new Http10ClientEngine(clientOptions);
    }

    internal static IClientProtocolEngine CreateHttp11Engine(Action<Http1ClientOptions>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http1);
        return new Http11ClientEngine(clientOptions);
    }

    internal static IClientProtocolEngine CreateHttp20Engine(Action<Http2ClientOptions>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http2);
        return new Http20ClientEngine(clientOptions);
    }

    internal static IClientProtocolEngine CreateHttp30Engine(Action<Http3ClientOptions>? configure = null)
    {
        var clientOptions = new TurboClientOptions();
        configure?.Invoke(clientOptions.Http3);
        return new Http30ClientEngine(clientOptions);
    }

    internal async Task<(HttpResponseMessage Response, string RawRequest)> SendScriptedWithCaptureAsync(
        IClientProtocolEngine engine,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory)
    {
        var stage = CreateAccumulatingScriptedConnection(responseFactory);
        var flow = engine.CreateFlow().Join(stage.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (stage.TryGetOutbound(out var outbound))
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
        }

        return (response, rawBuilder.ToString());
    }
}