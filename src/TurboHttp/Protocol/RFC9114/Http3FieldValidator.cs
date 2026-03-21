using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// RFC 9114 §4.2 — Validates HTTP/3 field names and values.
///
/// HTTP/3 uses QPACK header compression which transmits field names as lowercase strings.
/// This validator enforces:
/// - Field names MUST be lowercase (uppercase characters are malformed)
/// - Connection-specific headers are forbidden (Connection, Transfer-Encoding, Upgrade,
///   Proxy-Connection, Keep-Alive)
/// - The TE header is only allowed with value "trailers"
///
/// These rules apply to both request and response header fields.
/// </summary>
public static class Http3FieldValidator
{
    /// <summary>
    /// Validates all field names and values in the header list.
    /// Throws <see cref="Http3ConnectionException"/> with <see cref="Http3ErrorCode.MessageError"/>
    /// if any field violates RFC 9114 §4.2 rules.
    /// </summary>
    /// <param name="headers">The header field list to validate.</param>
    public static void Validate(IReadOnlyList<(string Name, string Value)> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];

            // Skip pseudo-headers — validated separately by pseudo-header validators
            if (name.Length > 0 && name[0] == ':')
            {
                continue;
            }

            ValidateFieldName(name);
            ValidateConnectionSpecific(name, value);
        }
    }

    /// <summary>
    /// Validates that a field name contains no uppercase ASCII characters.
    /// RFC 9114 §4.2: "A request or response that contains a field with an
    /// uppercase character in the field name MUST be treated as malformed."
    /// </summary>
    internal static void ValidateFieldName(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c >= 'A' && c <= 'Z')
            {
                throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                    $"RFC 9114 §4.2: Field name '{name}' contains uppercase character '{c}' at position {i}");
            }
        }
    }

    /// <summary>
    /// Validates that the field is not a connection-specific header forbidden in HTTP/3.
    /// RFC 9114 §4.2: "An intermediary transforming an HTTP/1.x message to HTTP/3
    /// MUST remove connection-specific header fields."
    ///
    /// The TE header is a special case: it is allowed only with the value "trailers"
    /// (RFC 9114 §4.2, RFC 9110 §7.6.1).
    /// </summary>
    internal static void ValidateConnectionSpecific(string name, string value)
    {
        if (string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Connection header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Transfer-Encoding header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "upgrade", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Upgrade header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "proxy-connection", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Proxy-Connection header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "keep-alive", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Keep-Alive header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "te", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(value, "trailers", StringComparison.OrdinalIgnoreCase))
            {
                throw new Http3ConnectionException(Http3ErrorCode.MessageError,
                    $"RFC 9114 §4.2: TE header is only allowed with value 'trailers', got '{value}'");
            }
        }
    }
}
