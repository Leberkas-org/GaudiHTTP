using System.Text;

namespace TurboHTTP.Protocol.Http10;

internal static class HeaderDecoder
{
    private static readonly Encoding Iso88591 = Encoding.GetEncoding("iso-8859-1");

    internal static Dictionary<string, List<string>> Parse(string[] lines, int maxHeaderSize, int maxTotalHeaderSize)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? lastHeader = null;
        var totalSize = 0;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            // Obs-fold continuation (RFC 1945 §4.2): line starting with SP or HT
            if ((rawLine[0] == ' ' || rawLine[0] == '\t') && lastHeader != null)
            {
                var lastValues = headers[lastHeader];
                var lastValue = lastValues[^1];
                var foldedValue = lastValue + " " + rawLine.Trim();
                lastValues[^1] = foldedValue;

                // Re-check single header size after fold: name + ": " + updated value
                var foldedHeaderSize = Iso88591.GetByteCount(lastHeader)
                    + 2 // ": "
                    + Iso88591.GetByteCount(foldedValue);
                if (foldedHeaderSize > maxHeaderSize)
                {
                    throw new HttpDecoderException(HttpDecoderError.HeaderTooLarge,
                        $"Header '{lastHeader}' is {foldedHeaderSize} bytes; limit is {maxHeaderSize}.");
                }

                // Add fold contribution to total
                var foldContribution = Iso88591.GetByteCount(rawLine.Trim()) + 1; // " " + trimmed
                totalSize += foldContribution;
                if (totalSize > maxTotalHeaderSize)
                {
                    throw new HttpDecoderException(HttpDecoderError.TotalHeadersTooLarge,
                        $"Total header size is {totalSize} bytes; limit is {maxTotalHeaderSize}.");
                }

                continue;
            }

            var colon = rawLine.IndexOf(':');
            if (colon <= 0)
            {
                throw new HttpDecoderException(HttpDecoderError.InvalidHeader);
            }

            var name = rawLine[..colon];

            // Validate header name: no spaces allowed
            if (name.Contains(' '))
            {
                throw new HttpDecoderException(HttpDecoderError.InvalidFieldName);
            }

            name = name.Trim();
            var value = rawLine[(colon + 1)..].Trim();

            // Check single header size: name + ": " + value
            var headerSize = Iso88591.GetByteCount(name)
                + 2 // ": "
                + Iso88591.GetByteCount(value);
            if (headerSize > maxHeaderSize)
            {
                throw new HttpDecoderException(HttpDecoderError.HeaderTooLarge,
                    $"Header '{name}' is {headerSize} bytes; limit is {maxHeaderSize}.");
            }

            totalSize += headerSize;
            if (totalSize > maxTotalHeaderSize)
            {
                throw new HttpDecoderException(HttpDecoderError.TotalHeadersTooLarge,
                    $"Total header size is {totalSize} bytes; limit is {maxTotalHeaderSize}.");
            }

            if (!headers.TryGetValue(name, out var value1))
            {
                value1 = [];
                headers[name] = value1;
            }

            value1.Add(value);
            lastHeader = name;
        }

        return headers;
    }

    internal static int? ExtractContentLength(Dictionary<string, List<string>> headers)
    {
        if (!headers.TryGetValue(WellKnownHeaders.Names.ContentLength, out var clValues) ||
            clValues.Count == 0)
        {
            return null;
        }

        // RFC 1945: Multiple Content-Length with different values is an error
        if (clValues.Count > 1)
        {
            var first = clValues[0];
            for (var i = 1; i < clValues.Count; i++)
            {
                if (!clValues[i].Equals(first, StringComparison.Ordinal))
                {
                    throw new HttpDecoderException(HttpDecoderError.MultipleContentLengthValues,
                        $"Values '{first}' and '{clValues[i]}' conflict.");
                }
            }
        }

        if (!int.TryParse(clValues[0], out var len))
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidContentLength,
                $"Value: '{clValues[0]}'.");
        }

        if (len < 0)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidContentLength,
                $"Value {len} is negative.");
        }

        return len;
    }
}
