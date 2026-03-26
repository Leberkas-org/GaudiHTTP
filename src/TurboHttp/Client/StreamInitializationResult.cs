using System;
using Akka.Actor;

namespace TurboHttp.Client;

/// <summary>
/// Result of <see cref="IClientStreamOwner.InitializeStreamAsync"/>. Pattern match on
/// <see cref="Success"/> or <see cref="Failed"/> to handle the outcome.
/// </summary>
public abstract record StreamInitializationResult
{
    private StreamInitializationResult() { }

    /// <summary>
    /// The stream instance was successfully created and the pipeline is materialized.
    /// </summary>
    /// <param name="InstanceRef">The actor reference of the running <c>ClientStreamInstance</c>.</param>
    public sealed record Success(IActorRef InstanceRef) : StreamInitializationResult;

    /// <summary>
    /// Stream initialization failed after all retry attempts were exhausted.
    /// </summary>
    /// <param name="Reason">The exception that caused the final failure.</param>
    public sealed record Failed(Exception Reason) : StreamInitializationResult;
}
