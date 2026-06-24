using System.Text;

namespace GaudiHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §11 — Represents an HTTP authentication challenge from a server.
/// Includes the authentication scheme, realm, optional token68, and additional parameters.
/// </summary>
internal sealed class AuthChallenge
{
    /// <summary>
    /// The authentication scheme (e.g., "Basic", "Bearer", "Digest").
    /// Stored in lowercase for case-insensitive comparison.
    /// </summary>
    public required string Scheme { get; init; }

    /// <summary>
    /// The realm parameter from the challenge, typically describing the protected area.
    /// May be null if not provided.
    /// </summary>
    public string? Realm { get; init; }

    /// <summary>
    /// Token68 format for authentication (used in some schemes like Bearer).
    /// May be null if not present.
    /// </summary>
    public string? Token68 { get; init; }

    /// <summary>
    /// Additional authentication parameters (e.g., algorithm, nonce for Digest).
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Parses a single authentication challenge from a string.
    /// Extracts scheme, realm, token68, and additional parameters.
    /// </summary>
    /// <param name="input">The authentication challenge string (e.g., "Basic realm=\"example.com\"").</param>
    /// <returns>An AuthChallenge instance parsed from the input.</returns>
    public static AuthChallenge Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new AuthChallenge { Scheme = "" };
        }

        var trimmed = input.AsSpan().Trim();
        var spaceIndex = trimmed.IndexOf(' ');

        var scheme = spaceIndex < 0
            ? trimmed.ToString()
            : trimmed[..spaceIndex].ToString();

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? realm = null;
        string? token68 = null;

        if (spaceIndex > 0)
        {
            var rest = trimmed[(spaceIndex + 1)..].Trim();

            if (!rest.Contains('='))
            {
                token68 = rest.ToString();
            }
            else
            {
                var parts = rest.ToString().Split(',');

                foreach (var part in parts)
                {
                    var trimmedPart = part.AsSpan().Trim();
                    var eqIndex = trimmedPart.IndexOf('=');

                    if (eqIndex > 0)
                    {
                        var key = trimmedPart[..eqIndex].Trim().ToString();
                        var value = trimmedPart[(eqIndex + 1)..].Trim().ToString();

                        value = UnquoteValue(value);

                        if (key.Equals("realm", StringComparison.OrdinalIgnoreCase))
                        {
                            realm = value;
                        }
                        else
                        {
                            parameters[key] = value;
                        }
                    }
                }
            }
        }

        return new AuthChallenge
        {
            Scheme = scheme.ToLowerInvariant(),
            Realm = realm,
            Token68 = token68,
            Parameters = parameters
        };
    }

    /// <summary>
    /// Parses multiple authentication challenges from a comma-separated string.
    /// </summary>
    /// <param name="input">The comma-separated authentication challenges.</param>
    /// <returns>A list of AuthChallenge instances.</returns>
    public static IReadOnlyList<AuthChallenge> ParseList(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var challenges = new List<AuthChallenge>();
        var currentChallenge = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                currentChallenge.Append(ch);
            }
            else if (ch == ',' && !inQuotes)
            {
                var challenge = currentChallenge.ToString().Trim();
                if (!string.IsNullOrEmpty(challenge))
                {
                    challenges.Add(Parse(challenge));
                }

                currentChallenge.Clear();
            }
            else
            {
                currentChallenge.Append(ch);
            }
        }

        var lastChallenge = currentChallenge.ToString().Trim();
        if (!string.IsNullOrEmpty(lastChallenge))
        {
            challenges.Add(Parse(lastChallenge));
        }

        return challenges;
    }

    /// <summary>
    /// Formats authentication credentials as "Scheme token".
    /// </summary>
    /// <param name="scheme">The authentication scheme (e.g., "Bearer", "Basic").</param>
    /// <param name="token">The authentication token or credentials.</param>
    /// <returns>The formatted credentials string.</returns>
    public static string FormatCredentials(string scheme, string token)
    {
        return string.Concat(scheme, " ", token);
    }

    private static string UnquoteValue(string value)
    {
        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            return value[1..^1];
        }

        return value;
    }
}