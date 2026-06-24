using System.Net;

namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §15.1: Overview of Status Codes
/// Classifies HTTP status codes by their first digit and caching properties.
/// </summary>
internal static class StatusCodeSemantics
{
    /// <summary>
    /// Classifies a status code into one of five classes based on the first digit.
    /// RFC 9110 §15.1: Valid range is 100-599.
    /// Unrecognized codes are treated as their class equivalent (e.g., 418 as 400).
    /// </summary>
    public static StatusCodeClass Classify(HttpStatusCode code)
    {
        var codeValue = (int)code;

        if (codeValue is < 100 or > 599)
        {
            return StatusCodeClass.ServerError;
        }

        return (codeValue / 100) switch
        {
            1 => StatusCodeClass.Informational,
            2 => StatusCodeClass.Successful,
            3 => StatusCodeClass.Redirection,
            4 => StatusCodeClass.ClientError,
            _ => StatusCodeClass.ServerError
        };
    }

    /// <summary>
    /// RFC 9110 §15.1: Heuristically cacheable status codes.
    /// These responses can be reused by a cache with heuristic expiration
    /// unless otherwise indicated by method definition or explicit cache controls.
    /// </summary>
    public static bool IsHeuristicallyCacheable(HttpStatusCode code)
    {
        return code switch
        {
            HttpStatusCode.OK => true,                      // 200
            HttpStatusCode.NonAuthoritativeInformation => true,  // 203
            HttpStatusCode.NoContent => true,               // 204
            HttpStatusCode.PartialContent => true,          // 206
            HttpStatusCode.MultipleChoices => true,         // 300
            HttpStatusCode.MovedPermanently => true,        // 301
            HttpStatusCode.PermanentRedirect => true,       // 308
            HttpStatusCode.NotFound => true,                // 404
            HttpStatusCode.MethodNotAllowed => true,        // 405
            HttpStatusCode.Gone => true,                    // 410
            HttpStatusCode.RequestUriTooLong => true,       // 414
            HttpStatusCode.NotImplemented => true,          // 501
            _ => false
        };
    }
}

/// <summary>
/// HTTP response status code class based on first digit (RFC 9110 §15.1).
/// </summary>
internal enum StatusCodeClass
{
    Informational = 1,
    Successful = 2,
    Redirection = 3,
    ClientError = 4,
    ServerError = 5
}
