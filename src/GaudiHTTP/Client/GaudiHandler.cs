namespace GaudiHTTP.Client;

/// <summary>
/// Base class for pipeline handlers that inspect or transform requests and responses.
/// Override <see cref="ProcessRequest"/> and/or <see cref="ProcessResponse"/> to add
/// cross-cutting behavior such as authentication, logging, or header injection.
/// Register handlers via <c>AddHandler&lt;T&gt;</c> or <c>UseRequest</c>/<c>UseResponse</c>
/// on an <see cref="IGaudiHttpClientBuilder"/>; handlers run in FIFO registration order.
/// </summary>
public abstract class GaudiHandler
{
    /// <summary>
    /// Inspects or transforms <paramref name="request"/> before it is sent.
    /// The default implementation returns <paramref name="request"/> unchanged.
    /// </summary>
    public virtual HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        => request;

    /// <summary>
    /// Inspects or transforms <paramref name="response"/> after it is received.
    /// The default implementation returns <paramref name="response"/> unchanged.
    /// </summary>
    public virtual HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response)
        => response;
}
