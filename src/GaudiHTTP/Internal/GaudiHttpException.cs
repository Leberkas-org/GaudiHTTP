namespace GaudiHTTP.Internal;

/// <summary>
/// Base class for all GaudiHttp exceptions.
/// Catch this type to handle any error originating from the GaudiHttp library.
/// </summary>
internal abstract class GaudiHttpException : Exception
{
    protected GaudiHttpException(string message) : base(message)
    {
    }

    protected GaudiHttpException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base class for protocol-level exceptions (RFC violations, malformed frames, compression errors).
/// Catch this type to handle any protocol error across HTTP/1.x, HTTP/2, HTTP/3, HPACK, and QPACK.
/// </summary>
internal abstract class GaudiProtocolException : GaudiHttpException
{
    protected GaudiProtocolException(string message) : base(message)
    {
    }

    protected GaudiProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base class for transport-level exceptions (connection failures, abrupt disconnects).
/// Catch this type to handle any connection or transport error.
/// </summary>
internal abstract class GaudiTransportException(string message) : GaudiHttpException(message);

