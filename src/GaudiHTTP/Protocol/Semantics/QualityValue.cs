using System.Globalization;

namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §12.4.2 — Represents a value with an associated quality factor (q-value).
/// Used for content negotiation in Accept, Accept-Encoding, Accept-Language, etc.
/// Quality values range from 0.0 (not acceptable) to 1.0 (fully acceptable).
/// </summary>
internal readonly record struct QualityValue(string Value, double Quality) : IComparable<QualityValue>
{
    /// <summary>
    /// Returns true if the quality is 0.0, indicating not acceptable.
    /// </summary>
    public bool IsNotAcceptable => Quality == 0.0;

    /// <summary>
    /// Compares two QualityValue instances by quality in descending order.
    /// Higher quality values sort before lower ones.
    /// </summary>
    public int CompareTo(QualityValue other)
    {
        // Sort descending by quality (higher quality first)
        return other.Quality.CompareTo(Quality);
    }

    /// <summary>
    /// Parses a quality value string such as "text/html;q=0.5" or "gzip".
    /// If no quality factor is specified, defaults to 1.0.
    /// Quality values are clamped to [0, 1].
    /// </summary>
    /// <param name="input">The input string to parse (e.g., "text/html;q=0.5", "gzip").</param>
    /// <returns>A QualityValue with the parsed value and quality.</returns>
    public static QualityValue Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new QualityValue("", 1.0);
        }

        var trimmed = input.AsSpan().Trim();
        var semicolonIndex = trimmed.IndexOf(';');

        if (semicolonIndex < 0)
        {
            return new QualityValue(trimmed.ToString(), 1.0);
        }

        var value = trimmed[..semicolonIndex].Trim().ToString();
        var parameters = trimmed[(semicolonIndex + 1)..];

        var quality = 1.0;
        var paramSpan = parameters.Trim();

        var eqIndex = paramSpan.IndexOf('=');
        if (eqIndex > 0)
        {
            var paramName = paramSpan[..eqIndex].Trim();
            if (paramName.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                var qValueStr = paramSpan[(eqIndex + 1)..].Trim();
                if (double.TryParse(qValueStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var q))
                {
                    quality = q;
                }
            }
        }

        quality = Math.Clamp(quality, 0.0, 1.0);
        return new QualityValue(value, quality);
    }

    /// <summary>
    /// Parses a comma-separated list of quality values, sorts them by quality in descending order.
    /// </summary>
    /// <param name="input">The input string containing comma-separated quality values.</param>
    /// <returns>A sorted list of QualityValue instances.</returns>
    public static IReadOnlyList<QualityValue> ParseList(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var parts = input.Split(',');
        var values = new QualityValue[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            values[i] = Parse(parts[i].Trim());
        }

        Array.Sort(values);
        return values;
    }
}
