namespace TurboHTTP;

/// <summary>
/// Controls when HTTP/2 keep-alive PING frames are sent.
/// </summary>
public enum HttpKeepAlivePingPolicy
{
    /// <summary>
    /// Keep-alive PINGs are only sent while there are active streams on the connection.
    /// </summary>
    WithActiveRequests = 0,

    /// <summary>
    /// Keep-alive PINGs are sent for the entire lifetime of the connection.
    /// </summary>
    Always = 1
}
