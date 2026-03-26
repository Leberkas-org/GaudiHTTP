using System;
using Akka.Actor;

namespace TurboHttp.Client;

/// <summary>
/// Options for initializing a stream instance via <see cref="IClientStreamOwner.InitializeStreamAsync"/>.
/// </summary>
/// <param name="ClientOptions">Client configuration (timeouts, TLS, connection policy).</param>
/// <param name="RequestOptionsFactory">Factory that produces per-request transport options.</param>
/// <param name="SupervisorStrategy">
/// Optional custom supervisor strategy for the stream instance actor. When <see langword="null"/>,
/// the default <c>AllForOneStrategy</c> with 3 retries and exponential backoff (100ms, 500ms, 2s)
/// is used.
/// </param>
public sealed record StreamInitializationOptions(
    TurboClientOptions ClientOptions,
    Func<TurboRequestOptions> RequestOptionsFactory,
    SupervisorStrategy? SupervisorStrategy = null);
