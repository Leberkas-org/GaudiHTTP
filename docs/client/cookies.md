# Cookie Management

TurboHTTP cookie handling is opt-in. Call `.WithCookies()` on the client builder to enable it. Once enabled, when a server sends a `Set-Cookie` header, TurboHTTP stores it and attaches it to subsequent requests that match the cookie's domain and path.

## How It Works

The cookie lifecycle has two steps:

1. **Store** — after every response, TurboHTTP scans for `Set-Cookie` headers and adds matching cookies to an internal `CookieJar`.
2. **Inject** — before every outgoing request, TurboHTTP checks the jar for applicable cookies and adds them to the `Cookie` request header.

Both steps happen transparently inside the request pipeline. Cookies from a login response are automatically sent on the very next request to the same domain.

## Cookie Isolation

Cookies are disabled unless `.WithCookies()` is called on the builder. When enabled, each client gets its own isolated `CookieJar`. Cookies received by one client are never shared with another. This means:

- A client used for API calls and a client used for authentication do **not** share cookie state.
- Creating multiple clients for different services keeps their session cookies completely separate.

```csharp
// Enable cookies independently for each client
builder.Services.AddTurboHttpClient("api", ...).WithCookies();
builder.Services.AddTurboHttpClient("auth", ...).WithCookies();

// Each client now has its own isolated cookie jar
var apiClient = factory.CreateClient("api");
var authClient = factory.CreateClient("auth");
```

## Domain Matching

A cookie is only sent to the domain it was set for. TurboHTTP uses proper label-boundary matching — a cookie for `example.com` does not match `notexample.com`.

- **Host-only cookies** (no `Domain` attribute) — sent only to the exact host that set them.
- **Domain cookies** (`Domain=example.com`) — sent to `example.com` and all subdomains (`api.example.com`, `auth.example.com`, etc.).

```
Set-Cookie: token=abc123                   → host-only: api.example.com only
Set-Cookie: session=xyz; Domain=example.com → domain: example.com + all subdomains
```

## Path Matching

A cookie is only sent to URLs whose path starts with the cookie's path. More specific paths take priority — if two cookies match, the one with the longer path is sent first.

```
Set-Cookie: pref=dark; Path=/settings  → sent to /settings and /settings/theme, not to /api
Set-Cookie: session=xyz; Path=/        → sent to every path
```

## Cookie Attributes

### `Secure`

The cookie is only sent over HTTPS connections. Cookies marked `Secure` are silently withheld on plain `http://` requests.

```
Set-Cookie: token=abc; Secure   ← sent on https://, not http://
```

**Practical impact:** Always use `Secure` for session tokens and authentication cookies in production.

### `HttpOnly`

Marks a cookie as server-only — it cannot be read by client-side scripts. TurboHTTP stores and sends `HttpOnly` cookies normally; the attribute is informational for browsers.

```
Set-Cookie: session=xyz; HttpOnly
```

**Practical impact:** `HttpOnly` cookies are stored in the jar and injected into requests just like any other cookie.

### `SameSite`

Controls whether a cookie is sent with cross-site requests. Because a programmatic HTTP client has no inherent notion of "the current site", TurboHTTP treats every request as first-party (same-site) by default and sends matching cookies. To opt into `SameSite` enforcement, tell TurboHTTP which site is initiating the request:

```csharp
var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/transfer")
    .WithFirstPartyContext(new Uri("https://app.other.com/"));
```

When a first-party context is set and the target is **cross-site** relative to it, TurboHTTP applies the policy below before injecting cookies:

| Value      | Cross-site behaviour                                             |
| ---------- | --------------------------------------------------------------- |
| `Strict`   | Never sent cross-site                                           |
| `Lax`      | Sent cross-site only for safe top-level navigations (GET/HEAD)  |
| `None`     | Always sent (requires `Secure`)                                 |
| _(absent)_ | No restriction — sent like `None`                              |

Two requests are considered same-site when they share the same registrable domain (e.g. `app.example.com` and `api.example.com`). TurboHTTP does not bundle a public-suffix list, so multi-level suffixes like `co.uk` are compared on their last two labels.

## Expiration

A cookie's lifetime is controlled by two attributes. `Max-Age` takes precedence over `Expires` when both are present.

### `Max-Age`

Lifetime in seconds from the time the response was received.

```
Set-Cookie: promo=sale; Max-Age=3600   ← expires in 1 hour
Set-Cookie: cart=empty; Max-Age=0      ← deleted immediately
```

### `Expires`

Absolute expiry date in HTTP-date format.

```
Set-Cookie: pref=dark; Expires=Fri, 20 Jun 2026 12:00:00 GMT
```

### Session cookies

A cookie with no `Max-Age` and no `Expires` is a **session cookie** — it lives for the duration of the current client instance and is discarded when the `TurboHttpClient` is disposed.

```
Set-Cookie: sid=abc123   ← no expiry: lasts until the client is disposed
```

## Sharing a Cookie Store

When `.WithCookies()` is called without arguments, each client gets its own isolated in-memory store. To share cookies across multiple clients — for example, so that a login performed by one client is visible to another — implement `ICookieStore` and pass the same instance to each:

```csharp
using TurboHTTP.Features.Cookies;

// Your thread-safe ICookieStore implementation
ICookieStore sharedStore = new MySharedCookieStore();

builder.Services.AddTurboHttpClient("auth", options =>
{
    options.BaseAddress = new Uri("https://auth.example.com");
})
.WithCookies(sharedStore);

builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCookies(sharedStore);
```

A cookie set during login on the `auth` client will now be available to the `api` client.

::: warning Thread safety
`ICookieStore` implementations are not required to be thread-safe when used by a single client — the request pipeline accesses the store on one logical thread at a time. However, when the **same store instance is shared across multiple clients**, those pipelines run concurrently and can access the store simultaneously. In that case your implementation must handle concurrent reads and writes safely.
:::

::: info How it works
See [Architecture: Request Pipeline](/architecture/pipeline) to understand how this feature fits into the processing pipeline.
:::
