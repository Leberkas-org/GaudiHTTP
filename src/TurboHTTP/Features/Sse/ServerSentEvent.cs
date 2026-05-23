namespace TurboHTTP.Features.Sse;

public sealed record ServerSentEvent(
    string Data,
    string? EventType = null,
    string? Id = null,
    TimeSpan? Retry = null);
