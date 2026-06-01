namespace TurboHTTP.Client;

/// <summary>
/// HTTP/2-specific configuration options.
/// Defaults are aligned with <c>System.Net.Http.SocketsHttpHandler</c>.
/// </summary>
public sealed class Http2ClientOptions
{
    /// <summary>
    /// Maximum number of concurrent TCP connections per server for HTTP/2.
    /// HTTP/2 multiplexes many streams over a single connection, so far fewer connections
    /// are needed compared to HTTP/1.x. Default is 6 to spread load across multiple
    /// actor turns at medium concurrency (CL=8–128).
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 6;

    /// <summary>
    /// Maximum number of concurrent HTTP/2 streams per connection.
    /// Controls how many requests can be in-flight simultaneously on a single H/2 TCP connection,
    /// enabling true request multiplexing within each substream.
    /// Default is 100.
    /// </summary>
    public int MaxConcurrentStreams { get; set; } = 100;

    /// <summary>
    /// Connection-level flow control window size in bytes (RFC 9113 §6.9).
    /// Advertised via WINDOW_UPDATE on stream 0 during the connection preface.
    /// Default is 64 MB.
    /// </summary>
    public int InitialConnectionWindowSize { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Per-stream initial flow control window size in bytes (RFC 9113 §6.9.2).
    /// Advertised via SETTINGS_INITIAL_WINDOW_SIZE in the connection preface.
    /// This is the starting window for adaptive scaling (when <see cref="EnableAdaptiveWindowScaling"/> is true),
    /// or the static window when scaling is disabled. Default is 65,535 (the RFC protocol default).
    /// When adaptive scaling is enabled, the window grows up to <see cref="MaxStreamWindowSize"/>.
    /// </summary>
    public int InitialStreamWindowSize { get; set; } = 65535;

    /// <summary>
    /// Upper bound the per-stream receive window may grow to under adaptive scaling, in bytes.
    /// Default is 16 MB.
    /// </summary>
    public int MaxStreamWindowSize { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Threshold multiplier for adaptive window growth. Higher values grow the window less eagerly.
    /// Default is 1.0.
    /// </summary>
    public double WindowScaleThresholdMultiplier { get; set; } = 1.0;

    /// <summary>
    /// Enables client-side adaptive (BDP-based) receive-window scaling. When false, the per-stream
    /// window stays static at <see cref="InitialStreamWindowSize"/>. Default is true.
    /// </summary>
    public bool EnableAdaptiveWindowScaling { get; set; } = true;

    /// <summary>
    /// Maximum HTTP/2 frame payload size in bytes the client is willing to RECEIVE (RFC 9113 §4.2).
    /// Advertised to the server via SETTINGS_MAX_FRAME_SIZE in the connection preface.
    /// This does NOT control the size of frames the client sends — outgoing frames are bounded by
    /// the server's advertised limit (default 16,384 until the server's SETTINGS arrive).
    /// Default is 64 KB. Valid range is [16,384, 16,777,215]; 16,384 is the RFC minimum/default.
    /// SocketsHttpHandler does not expose this knob.
    /// </summary>
    public int MaxFrameSize { get; set; } = 64 * 1024;

    /// <summary>
    /// HPACK dynamic table size in bytes (RFC 7541 §4.2).
    /// Advertised via SETTINGS_HEADER_TABLE_SIZE in the connection preface.
    /// Default is 64 KB. The RFC 7541 protocol default is 4,096; TurboHTTP uses a larger table
    /// for more aggressive header compression.
    /// </summary>
    public int HeaderTableSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum combined size (in bytes) of a decoded HPACK response header list the client will accept
    /// (RFC 9113 §6.5.2, SETTINGS_MAX_HEADER_LIST_SIZE). Guards against header-bomb responses.
    /// Default is 64 KB.
    /// </summary>
    public int MaxResponseHeaderListSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum number of reconnect attempts when a TCP connection drops with in-flight requests.
    /// After this many failed reconnects, the connection stage fails with an exception.
    /// Default is 3.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// Delay before sending a keep-alive PING frame when no frames have been received.
    /// Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable keep-alive pings (default).
    /// </summary>
    public TimeSpan KeepAlivePingDelay { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Timeout for keep-alive PING acknowledgment. If no frame is received within this
    /// duration after a PING is sent, the connection is closed and reconnected.
    /// Default is 20 seconds.
    /// </summary>
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Controls when keep-alive PINGs are sent.
    /// <see cref="HttpKeepAlivePingPolicy.Always"/> sends pings for the connection lifetime;
    /// <see cref="HttpKeepAlivePingPolicy.WithActiveRequests"/> only while streams are active.
    /// Default is <see cref="HttpKeepAlivePingPolicy.Always"/>.
    /// </summary>
    public HttpKeepAlivePingPolicy KeepAlivePingPolicy { get; set; } = HttpKeepAlivePingPolicy.Always;
}