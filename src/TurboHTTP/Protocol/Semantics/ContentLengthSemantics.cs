namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9112 §6.2-6.3: Content-Length Header Semantics
/// Handles parsing and validation of Content-Length header field values
/// and determines when message bodies are required based on status codes.
/// </summary>
internal static class ContentLengthSemantics
{
    /// <summary>
    /// RFC 9112 §6.2: Parse a Content-Length header field value.
    /// The value must be a non-negative decimal integer with no spaces or other characters.
    /// </summary>
    public static bool TryParse(string value, out long length)
    {
        length = 0;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!long.TryParse(value, out var parsedValue))
        {
            return false;
        }

        if (parsedValue < 0)
        {
            return false;
        }

        length = parsedValue;
        return true;
    }

    /// <summary>
    /// RFC 9112 §6.3: Determine if a message body is required based on status code and method.
    ///
    /// Message bodies are NOT required for:
    /// - Any response to a HEAD request
    /// - 1xx (Informational) responses
    /// - 204 (No Content) responses
    /// - 304 (Not Modified) responses
    /// - 2xx responses to CONNECT requests
    ///
    /// Message bodies MAY be present for all other responses.
    /// </summary>
    public static bool BodyRequired(System.Net.HttpStatusCode statusCode, string method)
    {
        var code = (int)statusCode;

        if (method == WellKnownHeaders.Head)
        {
            return false;
        }

        switch (code)
        {
            case >= 100 and < 200:
            case 204:
            case 304:
            case >= 200 and < 300 when method == WellKnownHeaders.Connect:
                return false;
            default:
                return true;
        }
    }
}