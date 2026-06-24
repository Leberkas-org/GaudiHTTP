# Using with ASP.NET Core

GaudiHTTP replaces Kestrel as the transport layer. Everything above the transport — middleware, routing, dependency injection, authentication — is standard ASP.NET Core. This page confirms which patterns work and highlights what's different.

## The Key Idea

| Layer | What handles it |
|-------|----------------|
| **Your Application Code** | Middleware, routing, controllers, minimal APIs |
| **ASP.NET Core Hosting** | `IHost`, `IHttpApplication`, `HostingApplication` |
| **GaudiHTTP Server** | `ApplicationBridgeStage`, protocol engines (H1/H2/H3), actor hierarchy, TCP/QUIC transport |

GaudiHTTP sits below the `IHttpApplication&lt;TContext&gt;` boundary. When a request arrives, GaudiHTTP decodes it into an `IFeatureCollection` and hands it to ASP.NET Core's `HostingApplication`, which runs your middleware pipeline.

## Middleware

Standard ASP.NET Core middleware works without changes:

```csharp
var app = builder.Build();

app.UseExceptionHandler("/error");
app.UseHsts();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Powered-By", "GaudiHTTP");
    await next(context);
});
```

## Routing

All ASP.NET Core routing patterns work:

```csharp
// Minimal APIs
app.MapGet("/", () => "Hello");
app.MapPost("/users", (CreateUserRequest req) => Results.Created($"/users/{req.Id}", req));

// Route groups
var api = app.MapGroup("/api/v1");
api.MapGet("/status", () => Results.Ok("healthy"));

// Controllers
app.MapControllers();
```

## Dependency Injection

Standard service registration and injection:

```csharp
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

app.MapGet("/users/{id}", async (int id, IUserRepository repo) =>
{
    var user = await repo.GetByIdAsync(id);
    return user is not null ? Results.Ok(user) : Results.NotFound();
});
```

## Configuration & Options

Standard `IOptions<T>` patterns, environment variables, and `appsettings.json` all work:

```csharp
builder.Services.Configure<MyAppOptions>(builder.Configuration.GetSection("MyApp"));

app.MapGet("/config", (IOptions<MyAppOptions> opts) => Results.Ok(opts.Value));
```

## Authentication & Authorization

Standard ASP.NET Core auth:

```csharp
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/secure", () => "Protected").RequireAuthorization();
```

## Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("db", () => HealthCheckResult.Healthy());

app.MapHealthChecks("/health");
```

## What's Different from Kestrel

Most things are identical. These are the differences:

### Connection Model

Kestrel uses thread-pool-based connection handling. GaudiHTTP creates an Akka.NET actor per connection. This means:

- Each connection has isolated state — no shared mutable state between connections
- Supervision strategies handle connection failures automatically
- Backpressure flows from `ApplicationBridgeStage` through the protocol engine to the transport

### ActorSystem

GaudiHTTP creates or reuses an `ActorSystem`:

- If your DI container already has an `ActorSystem` registered (via Akka.Hosting), GaudiHTTP reuses it
- If not, GaudiHTTP creates its own `ActorSystem` named `gaudi-server`

```csharp
// Share an ActorSystem with Akka.Hosting
builder.Services.AddAkka("my-system", configurationBuilder =>
{
    // your Akka.NET config
});

builder.Host.UseGaudiHttp(options =>
{
    options.ListenLocalhost(5000);
});
// GaudiHTTP reuses "my-system"
```

### Handler Timeout

GaudiHTTP enforces a per-request handler timeout (default 30s). If your handler doesn't complete within `HandlerTimeout + HandlerGracePeriod`, the request gets a 503 response:

```csharp
builder.Host.UseGaudiHttp(options =>
{
    options.HandlerTimeout = TimeSpan.FromSeconds(60);
    options.HandlerGracePeriod = TimeSpan.FromSeconds(5); // default
});
```

### Graceful Shutdown

GaudiHTTP uses Akka Coordinated Shutdown instead of `IHostApplicationLifetime`. See [Hosting & Lifecycle](./hosting) for details.

## Features That Work Out of the Box

These ASP.NET Core features require no special configuration with GaudiHTTP:

- Minimal APIs and controllers
- Model binding and validation
- `IResult` return types
- Exception handling middleware
- Static files
- CORS
- Response compression
- Response caching
- Request logging / `W3CLogging`
- OpenTelemetry / diagnostics
