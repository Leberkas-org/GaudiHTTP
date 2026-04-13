namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Immutable configuration for an HTTP/3 connection.
/// </summary>
public sealed record Http3ConnectionConfig(
    int MaxFieldSectionSize = 65536,
    int QpackMaxTableCapacity = 4096,
    int QpackBlockedStreams = 100,
    TimeSpan IdleTimeout = default,
    int MaxReconnectAttempts = 3,
    bool AllowServerPush = false,
    bool AllowEarlyData = false,
    bool AllowConnectionMigration = true);
