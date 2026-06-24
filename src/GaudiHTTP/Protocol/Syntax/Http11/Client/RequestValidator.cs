using System.Net.Http.Headers;

namespace GaudiHTTP.Protocol.Syntax.Http11.Client;

/// <summary>
/// RFC 9112 compliant HTTP/1.1 request validator.
/// Validates method, headers, and header values for RFC compliance and injection prevention.
/// </summary>
internal static class RequestValidator
{
    /// <summary>
    /// Validates an HTTP request for RFC 9112 compliance and injection prevention.
    /// </summary>
    /// <param name="request">The HTTP request to validate</param>
    /// <exception cref="ArgumentException">If validation fails</exception>
    public static void Validate(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate method before encoding
        ValidateMethod(request.Method.Method);

        // Validate all headers (injection prevention + RFC compliance)
        ValidateHeaders(request.Headers);
        if (request.Content != null)
        {
            ValidateHeaders(request.Content.Headers);
        }
    }

    private static void ValidateMethod(string method)
    {
        if (method.AsSpan().IndexOfAnyInRange('a', 'z') >= 0)
        {
            throw new ArgumentException($"HTTP/1.1 method must be uppercase: {method}", nameof(method));
        }
    }

    private static void ValidateHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        foreach (var header in headers)
        {
            foreach (var value in header.Value)
            {
                ValidateHeaderValue(header.Key, value);
            }
        }
    }

    private static void ValidateHeaders(HttpContentHeaders headers)
    {
        foreach (var header in headers)
        {
            foreach (var value in header.Value)
            {
                ValidateHeaderValue(header.Key, value);
            }
        }
    }

    private static void ValidateHeaderValue(string name, string value)
    {
        if (value.AsSpan().ContainsAny('\r', '\n', '\0'))
        {
            throw new ArgumentException($"Header '{name}' contains invalid characters (CR/LF/NUL)", name);
        }

        if (name.Equals(WellKnownHeaders.Range, StringComparison.OrdinalIgnoreCase))
        {
            ValidateRangeValue(value);
        }
    }

    private static void ValidateRangeValue(string value)
    {
        // RFC 9110 §14.1.1: bytes-range-spec = first-byte-pos "-" [last-byte-pos]
        // suffix-byte-range-spec = "-" suffix-length
        // All positions must consist only of DIGIT characters.
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid Range header value: '{value}' (must start with 'bytes=')", "Range");
        }

        var rangeSpec = value["bytes=".Length..];
        var ranges = rangeSpec.Split(',');

        foreach (var range in ranges)
        {
            var trimmed = range.AsSpan().Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            var dashIdx = trimmed.IndexOf('-');
            if (dashIdx < 0)
            {
                throw new ArgumentException($"Invalid Range header value: '{value}' (missing '-' in range spec)", "Range");
            }

            var first = trimmed[..dashIdx];
            var last = trimmed[(dashIdx + 1)..];

            if (first.IsEmpty && last.IsEmpty)
            {
                throw new ArgumentException($"Invalid Range header value: '{value}' (empty range spec)", "Range");
            }

            foreach (var ch in first)
            {
                if (!char.IsAsciiDigit(ch))
                {
                    throw new ArgumentException($"Invalid Range header value: '{value}' (non-digit in byte position)", "Range");
                }
            }

            foreach (var ch in last)
            {
                if (!char.IsAsciiDigit(ch))
                {
                    throw new ArgumentException($"Invalid Range header value: '{value}' (non-digit in byte position)", "Range");
                }
            }
        }
    }
}
