# Scenarios

TurboHTTP combines a full HTTP stack with Akka Streams, giving you streaming, backpressure, and actor integration out of the box. These scenarios show what that looks like in practice — from actor-backed REST resources to real-time event streams.

---

## Entity Gateway

Building a REST API usually means writing controllers, wiring up routes, and manually dispatching to your domain layer. With `MapEntity`, you map HTTP verbs directly to actor interactions in a single fluent call.

```csharp
app.MapEntity<OrderId>("/api/orders/{id}", entity =>
{
    entity.OnGet(async (TurboHttpContext ctx, OrderId id) =>
    {
        var order = await ctx.ActorSystem
            .SelectActor<OrderActor>(id)
            .Ask<OrderResponse>(new GetOrder(id), ctx.RequestAborted);

        return Results.Ok(order);
    });

    entity.OnPost(async (TurboHttpContext ctx, OrderId id, CreateOrderRequest body) =>
    {
        ctx.ActorSystem
            .SelectActor<OrderActor>(id)
            .Tell(new CreateOrder(id, body));

        return Results.Created($"/api/orders/{id}", null);
    });

    entity.OnDelete(async (TurboHttpContext ctx, OrderId id) =>
    {
        ctx.ActorSystem
            .SelectActor<OrderActor>(id)
            .Tell(new DeleteOrder(id));

        return Results.NoContent();
    });
});
```

::: tip Key Insight
Each HTTP request routes to a specific actor instance by its typed key. The actor manages its own state and lifecycle — no shared database locks, no thread synchronization. Timeouts and error handling are built into the entity builder, so a slow or crashed actor returns a proper HTTP error without blocking other requests.
:::

---

## Real-Time SSE Streaming

Server-Sent Events let you push data to clients over a long-lived HTTP connection. TurboHTTP makes this trivial — return an Akka Streams `Source` wrapped in `TurboStreamResults.EventStream`, and the framework handles SSE framing, connection lifecycle, and backpressure for you.

```csharp
app.MapGet("/events/orders", (TurboHttpContext ctx) =>
{
    var events = ctx.ActorSystem
        .EventStream
        .AsSource<OrderEvent>()
        .Select(e => new ServerSentEvent(
            Data: e.ToJson(),
            EventType: e.GetType().Name,
            Id: e.OrderId.ToString()));

    return TurboStreamResults.EventStream(events);
});
```

::: tip Key Insight
The `Source` is materialized when the client connects and torn down when they disconnect. Backpressure flows end-to-end: if the client's network is slow, the stream slows down automatically — no manual buffering, no dropped events, no out-of-memory risk from unbounded queues.
:::

---

## Raw Byte Streaming

When you need to stream binary data — file downloads, video, sensor feeds — you want bytes to flow from the source to the network without piling up in memory. `TurboStreamResults.Stream` takes an Akka Streams `Source` of byte chunks and pipes it directly into the HTTP response body.

```csharp
app.MapGet("/files/{fileId}", (TurboHttpContext ctx, string fileId) =>
{
    var metadata = fileStore.GetMetadata(fileId);

    var bytes = FileIO.FromFile(metadata.Path, chunkSize: 8 * 1024)
        .Select(chunk => (ReadOnlyMemory<byte>)chunk.Memory);

    return TurboStreamResults.Stream(bytes, contentType: metadata.ContentType);
});
```

::: tip Key Insight
The `chunkSize` parameter controls how much data is in flight at any moment. Whether the file is 1 KB or 10 GB, memory usage stays constant — Akka Streams pulls the next chunk only when the previous one has been written to the network.
:::

---

## Client-Side Stream Consumption

The `TurboHttpClient` exposes its request/response pipeline as channels. Instead of awaiting one response at a time, you write requests into a `ChannelWriter` and read responses from a `ChannelReader` — turning HTTP into a stream you can process with `await foreach`.

```csharp
var client = factory.CreateClient("api");

var urls = Enumerable.Range(1, 100)
    .Select(i => new HttpRequestMessage(HttpMethod.Get, $"/api/products/{i}"));

foreach (var request in urls)
{
    await client.Requests.WriteAsync(request, ct);
}

client.Requests.Complete();

await foreach (var response in client.Responses.ReadAllAsync(ct))
{
    var product = await response.Content.ReadFromJsonAsync<Product>(ct);
    await ProcessProduct(product);
}
```

::: tip Key Insight
Over HTTP/2, all 100 requests multiplex on a single connection. Responses arrive as their streams complete — not in request order — so fast endpoints don't wait behind slow ones. The channel-based API makes this natural: write requests as fast as you want, consume responses as they show up.
:::

---

## Backpressure-Aware Pipeline

TurboHTTP doesn't just use Akka Streams for internal plumbing — it exposes the full operator toolkit for you to shape, merge, and throttle data before it hits the wire. Every operator in the pipeline participates in backpressure, from the data source all the way to the client's TCP receive window.

```csharp
app.MapGet("/metrics/live", (TurboHttpContext ctx) =>
{
    var cpuMetrics = ctx.ActorSystem
        .SelectActor<CpuMonitorActor>()
        .AsSource<MetricEvent>();

    var memoryMetrics = ctx.ActorSystem
        .SelectActor<MemoryMonitorActor>()
        .AsSource<MetricEvent>();

    var merged = cpuMetrics
        .Merge(memoryMetrics)
        .Throttle(100, TimeSpan.FromSeconds(1), ThrottleMode.Shaping)
        .Select(m => new ServerSentEvent(
            Data: m.ToJson(),
            EventType: m.Category));

    return TurboStreamResults.EventStream(merged);
});
```

::: tip Key Insight
`Merge`, `Throttle`, `Buffer`, `GroupBy`, `Broadcast` — these are standard Akka Streams operators, not TurboHTTP-specific APIs. Any stream processing graph you can build with Akka Streams plugs directly into an HTTP response. The pipeline handles framing, chunked transfer, and connection lifecycle automatically.
:::
