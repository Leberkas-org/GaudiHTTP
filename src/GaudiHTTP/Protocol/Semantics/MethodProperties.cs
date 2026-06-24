namespace GaudiHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §9.2: Common Method Properties
/// Defines method classification: safe, idempotent, and cacheable.
/// </summary>
internal static class MethodProperties
{
    /// <summary>
    /// RFC 9110 §9.2.1: Safe Methods
    /// A request method is "safe" if its defined semantics are essentially read-only.
    /// Safe methods: GET, HEAD, OPTIONS, TRACE
    /// </summary>
    public static bool IsSafe(HttpMethod method)
    {
        return method.Method switch
        {
            "GET" => true,
            "HEAD" => true,
            "OPTIONS" => true,
            "TRACE" => true,
            _ => false
        };
    }

    /// <summary>
    /// RFC 9110 §9.2.2: Idempotent Methods
    /// A request method is "idempotent" if the intended effect of multiple identical
    /// requests is the same as for a single request.
    /// Idempotent methods: GET, HEAD, OPTIONS, TRACE, PUT, DELETE
    /// </summary>
    public static bool IsIdempotent(HttpMethod method)
    {
        return method.Method switch
        {
            "GET" => true,
            "HEAD" => true,
            "OPTIONS" => true,
            "TRACE" => true,
            "PUT" => true,
            "DELETE" => true,
            _ => false
        };
    }

    /// <summary>
    /// RFC 9110 §9.2.3: Methods and Caching
    /// Defines which methods allow response caching. Only GET, HEAD, and POST
    /// are defined as cacheable in RFC 9110.
    /// </summary>
    public static bool IsCacheable(HttpMethod method)
    {
        return method.Method switch
        {
            "GET" => true,
            "HEAD" => true,
            "POST" => true,
            _ => false
        };
    }
}