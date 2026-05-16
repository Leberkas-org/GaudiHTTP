namespace TurboHTTP.Server.Lifecycle;

internal enum ConnectionCompletionReason
{
    Normal,
    Error,
    Timeout,
    ServerShutdown
}
