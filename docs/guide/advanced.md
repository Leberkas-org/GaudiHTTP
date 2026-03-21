# Advanced Usage

This page covers the channel-based streaming API, extension points for custom policies, and patterns for high-throughput workloads.

## Channel-Based API

In addition to `SendAsync`, TurboHttp exposes a lower-level channel API for scenarios where you want to stream requests and responses without `await`-ing each one individually.

```csharp
var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
}, actorSystem);

// Write requests to the input channel
ChannelWriter<HttpRequestMessage> requestWriter = client.Requests;

// Read responses from the output channel
ChannelReader<HttpResponseMessage> responseReader = client.Responses;
```

This API is useful when:
- You have a producer loop generating requests faster than you can await responses
- You want to decouple request creation from response processing
- You are integrating TurboHttp into a pipeline that already uses `System.Threading.Channels`

### High-Throughput Batch Pattern

Write requests from one task and read responses from another, running concurrently:

```csharp
var client = new TurboHttpClient(new TurboClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
    DefaultRequestVersion = HttpVersion.Version20,
}, actorSystem);

var ids = Enumerable.Range(1, 1000).ToList();

// Producer: write all requests without waiting for responses
var producer = Task.Run(async () =>
{
    foreach (var id in ids)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/items/{id}");
        await client.Requests.WriteAsync(request);
    }
    client.Requests.Complete();
});

// Consumer: process responses as they arrive
var consumer = Task.Run(async () =>
{
    await foreach (var response in client.Responses.ReadAllAsync())
    {
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{(int)response.StatusCode}: {body.Length} bytes");
    }
});

await Task.WhenAll(producer, consumer);
```

With HTTP/2, all 1000 requests flow over a single TCP connection as concurrent streams. With HTTP/1.1, they are serialised per connection but the producer/consumer split still keeps throughput high.

### Backpressure

The channel has a bounded capacity. If the connection cannot keep up with your producer, `WriteAsync` will pause automatically until there is room. You never drop requests — the channel applies backpressure instead.

## Extension Points

TurboHttp's built-in policies — retry, redirect, cookie, cache — are configured via the builder API. See the configuration guides for custom policy examples:

- **Custom retry logic**: Configure via `.WithRetry()` builder extension — see [Automatic Retries guide](./retries)
- **Custom redirect logic**: Configure via `.WithRedirect()` builder extension — see [Redirects guide](./redirects)
- **Custom cookie storage**: Provide a custom `CookieJar` instance via `.WithCookies()` — see [Cookie Management guide](./cookies)
- **Custom cache store**: Provide a custom `HttpCacheStore` instance or implement the cache interface — see [HTTP Caching guide](./caching)

The builder pattern eliminates boilerplate and ensures proper integration with the Akka.Streams pipeline.

## Extending the Pipeline with Akka.Streams

For advanced scenarios requiring low-level stream manipulation, TurboHttp's request pipeline is built on [Akka.Streams](https://getakka.net/articles/streams/introduction.html). You can insert custom Akka graph stages directly into the pipeline for request signing, telemetry, protocol translation, and other transformations.

A graph stage is a small, composable unit that transforms the stream:

```csharp
public sealed class RequestSigningStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    private readonly string _apiKey;

    public RequestSigningStage(string apiKey) => _apiKey = apiKey;

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }
        = new FlowShape<HttpRequestMessage, HttpRequestMessage>(
            new Inlet<HttpRequestMessage>("RequestSigning.In"),
            new Outlet<HttpRequestMessage>("RequestSigning.Out"));

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(Shape, _apiKey);

    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly string _apiKey;

        public Logic(FlowShape<HttpRequestMessage, HttpRequestMessage> shape, string apiKey)
            : base(shape)
        {
            _apiKey = apiKey;

            SetHandler(shape.Inlet, onPush: () =>
            {
                var request = Grab(shape.Inlet);
                request.Headers.Add("X-Api-Key", _apiKey);
                Push(shape.Outlet, request);
            });

            SetHandler(shape.Outlet, onPull: () => Pull(shape.Inlet));
        }
    }
}
```

Custom stages run inside the existing pipeline — they see every request before it is encoded and every response after it is decoded. This makes them suitable for:

- **Request signing** (HMAC, OAuth signatures)
- **Header injection** (correlation IDs, tenant context)
- **Response transformation** (unwrap envelopes, normalise status codes)
- **Observability** (latency histograms, request logging)

Akka.Streams guarantees backpressure through the entire stage chain — your custom stage will never be pushed faster than it can process.

For most use cases, the `TurboHandler` middleware API (see above) is simpler and sufficient. Use direct stage insertion only when you need access to the underlying stream topology.
