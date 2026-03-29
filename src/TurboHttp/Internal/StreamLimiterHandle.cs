using System;

namespace TurboHttp.Internal;

/// <summary>
/// Shared control handle for inter-stage communication between
/// <see cref="TurboHttp.Streams.Stages.Routing.Http20StreamLimiterStage"/> and
/// <see cref="TurboHttp.Streams.Stages.Decoding.Http20ConnectionStage"/>.
/// The limiter stage registers async callbacks during PreStart; the connection stage
/// invokes them when streams close or the server updates MAX_CONCURRENT_STREAMS.
/// </summary>
internal sealed class StreamLimiterHandle
{
    /// <summary>
    /// Invoked by <see cref="TurboHttp.Streams.Stages.Decoding.Http20ConnectionStage"/>
    /// when an active stream closes (END_STREAM or RST_STREAM received).
    /// The limiter stage uses this to decrement its active count and dequeue pending requests.
    /// </summary>
    public Action? OnStreamClosed { get; set; }

    /// <summary>
    /// Invoked by <see cref="TurboHttp.Streams.Stages.Decoding.Http20ConnectionStage"/>
    /// when the server sends a SETTINGS frame with a new MAX_CONCURRENT_STREAMS value.
    /// The limiter stage uses this to adjust its internal limit and potentially dequeue requests.
    /// </summary>
    public Action<int>? OnMaxConcurrentStreamsChanged { get; set; }
}
