namespace TurboHTTP.Client;

/// <summary>
/// HTTP/1.x-specific client configuration.
/// Controls connection pooling, pipelining depth, header limits, and automatic header injection.
/// Defaults are aligned with <c>System.Net.Http.SocketsHttpHandler</c> where applicable.
/// </summary>
public sealed class Http1ClientOptions
{
    /// <summary>
    /// Maximum response body size (in bytes) that is buffered fully in memory.
    /// Bodies larger than this are exposed as a streaming pipe. Default is 64 KiB.
    /// </summary>
    public int MaxBufferedResponseBodySize { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum number of concurrent TCP connections per server for HTTP/1.x.
    /// Each connection is managed as an independent substream.
    /// Default is 6 (matching browser defaults and RFC 9112 §9.4 guidance).
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 6;

    /// <summary>
    /// Maximum number of pipelined HTTP/1.1 requests allowed per connection before waiting for responses.
    /// Higher values increase throughput on high-latency links; lower values reduce head-of-line blocking.
    /// Default is 16.
    /// </summary>
    public int MaxPipelineDepth { get; set; } = 16;

    /// <summary>
    /// Maximum length of the response headers, in kilobytes (KB).
    /// This limits the combined size of all response header fields received from the server.
    /// Default is 64.
    /// </summary>
    public int MaxResponseHeadersLength { get; set; } = 64;

    /// <summary>
    /// Automatically add a Host header derived from the request URI if none is present.
    /// Default is true, matching standard HTTP/1.1 behavior.
    /// </summary>
    public bool AutoHost { get; set; } = true;

    /// <summary>
    /// Automatically add Accept-Encoding: gzip, deflate, br if no Accept-Encoding header is present.
    /// Default is true.
    /// </summary>
    public bool AutoAcceptEncoding { get; set; } = true;

    /// <summary>
    /// Maximum number of reconnect attempts when a TCP connection drops with in-flight requests.
    /// After this many failed reconnects, the connection stage fails with an exception.
    /// Default is 3.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// Maximum number of header fields accepted in an HTTP/1.x response.
    /// Guards against malicious servers flooding the client with header lines. Default is 100
    /// </summary>
    public int MaxResponseHeaderCount { get; set; } = 100;

    /// <summary>
    /// Maximum length (in bytes) of a single response status/header line in HTTP/1.x.
    /// Default is 8 KB.
    /// </summary>
    public int MaxResponseHeaderLineLength { get; set; } = 8 * 1024;

    /// <summary>
    /// Maximum length (in bytes) of the chunk extension on a single chunked-transfer chunk in an
    /// HTTP/1.1 response. Default is <see cref="int.MaxValue"/> (unbounded); set a smaller value to
    /// guard against malicious servers.
    /// </summary>
    public int MaxChunkExtensionLength { get; set; } = int.MaxValue;
}

