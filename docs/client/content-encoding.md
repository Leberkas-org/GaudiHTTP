# Content Encoding

TurboHTTP automatically decompresses compressed HTTP responses. When a server sends a `Content-Encoding` header, TurboHTTP decompresses the body transparently before returning the response — the calling code always receives plain, uncompressed content.

## Supported Encodings

| Encoding | Header token     | Notes                                                  |
| -------- | ---------------- | ------------------------------------------------------ |
| Gzip     | `gzip`, `x-gzip` | Most common; used by the majority of web servers       |
| Deflate  | `deflate`        | zlib-wrapped deflate format (RFC 1950)                 |
| Brotli   | `br`             | Best compression ratio; requires modern server support |
| Identity | `identity`       | No compression; body passed through unchanged          |

## How It Works

For HTTP/1.1 requests, TurboHTTP automatically adds `Accept-Encoding: gzip, deflate, br` to every outgoing request unless you have already set an `Accept-Encoding` header yourself. This tells the server which encodings the client can handle.

When a response arrives:

1. TurboHTTP reads the `Content-Encoding` header.
2. The body is decompressed using the appropriate algorithm.
3. The `Content-Encoding` header is removed from the response.
4. `Content-Length` is removed (the decompressed size is not known up front).

The final `HttpResponseMessage` you receive has an uncompressed body.

```csharp
var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/data"));

// Body is already decompressed — no manual handling needed
var text = await response.Content.ReadAsStringAsync();
```

## Stacked Encodings

TurboHTTP does not support stacked encodings (e.g., `Content-Encoding: gzip, br`). When a response carries multiple comma-separated encoding tokens, decompression will fail silently and the response body will be empty. To avoid this, do not advertise encoding combinations in `Accept-Encoding` that the server might stack — use a single preferred encoding instead.

## Unknown Encodings

If the server sends a `Content-Encoding` value that TurboHTTP does not recognise, the response is passed through unchanged with the compressed body intact. TurboHTTP only decompresses encodings it recognises (gzip, deflate, br, identity).

## Overriding Accept-Encoding

To request a specific encoding (or no compression at all), set `Accept-Encoding` on the request before sending:

```csharp
// Request no compression
var request = new HttpRequestMessage(HttpMethod.Get, "/data");
request.Headers.AcceptEncoding.ParseAdd("identity");

var response = await client.SendAsync(request);
```

```csharp
// Request only gzip
var request = new HttpRequestMessage(HttpMethod.Get, "/data");
request.Headers.AcceptEncoding.ParseAdd("gzip");

var response = await client.SendAsync(request);
```

When `AcceptEncoding` is already set on the request, TurboHTTP skips its automatic `Accept-Encoding` injection and uses your value instead.

## HTTP/2 Requests

For HTTP/2 requests, TurboHTTP does not automatically inject `Accept-Encoding`. If you want compressed responses over HTTP/2, add the header explicitly:

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "/data");
request.Version = HttpVersion.Version20;
request.Headers.AcceptEncoding.ParseAdd("gzip, br");

var response = await client.SendAsync(request);
// Body is decompressed automatically if the server compresses it
```

Decompression itself works the same regardless of protocol version — if the server includes a `Content-Encoding` header, TurboHTTP decodes the body.

::: info How it works
See [Architecture: Request Pipeline](/architecture/pipeline) to understand how this feature fits into the processing pipeline.
:::
