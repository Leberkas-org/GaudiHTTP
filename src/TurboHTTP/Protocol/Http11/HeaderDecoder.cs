namespace TurboHTTP.Protocol.Http11;

internal sealed class HeaderDecoder
{
    private readonly Dictionary<string, List<string>> _headerDict =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<List<string>> _listPool = new();

    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;
    private readonly int _maxHeaderCount;

    internal HeaderDecoder(int maxHeaderSize, int maxTotalHeaderSize, int maxHeaderCount)
    {
        _maxHeaderSize = maxHeaderSize;
        _maxTotalHeaderSize = maxTotalHeaderSize;
        _maxHeaderCount = maxHeaderCount;
    }

    internal Dictionary<string, List<string>> Parse(ReadOnlySpan<byte> data)
    {
        // Return List<string> instances from the previous call to the pool before clearing.
        foreach (var list in _headerDict.Values)
        {
            list.Clear();
            if (_listPool.Count < 32)
            {
                _listPool.Push(list);
            }
        }

        _headerDict.Clear();

        var pos = 0;
        var fieldCount = 0;
        var totalSize = 0;

        while (pos < data.Length)
        {
            var lineEnd = BufferSearch.FindCrlf(data, pos);
            if (lineEnd < 0 || lineEnd == pos)
            {
                break;
            }

            fieldCount++;
            var line = data[pos..lineEnd];
            var (nameStr, valueStr) = ValidateField(line, fieldCount, ref totalSize);

            if (!_headerDict.TryGetValue(nameStr, out var values))
            {
                values = _listPool.Count > 0 ? _listPool.Pop() : new List<string>(2);
                _headerDict[nameStr] = values;
            }

            values.Add(valueStr);

            pos = lineEnd + 2;
        }

        return _headerDict;
    }

    private (string name, string value) ValidateField(ReadOnlySpan<byte> line, int fieldCount, ref int totalSize)
    {
        // Security: enforce maximum header field count (prevents header flood attacks).
        if (fieldCount > _maxHeaderCount)
        {
            throw new HttpDecoderException(HttpDecoderError.TooManyHeaders,
                $"Received {fieldCount} fields; limit is {_maxHeaderCount}.");
        }

        // RFC 9112 §5.2: obs-fold (continuation line starting with SP/HT) is obsolete.
        if (line.Length > 0 && (line[0] == (byte)' ' || line[0] == (byte)'\t'))
        {
            throw new HttpDecoderException(HttpDecoderError.ObsoleteFoldingDetected);
        }

        var colonIdx = line.IndexOf((byte)':');

        // RFC 9112 §5.1: every header field MUST contain a colon.
        if (colonIdx <= 0)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidHeader);
        }

        var name = WellKnownHeaders.TrimOws(line[..colonIdx]);
        var value = WellKnownHeaders.TrimOws(line[(colonIdx + 1)..]);

        var nameStr = WellKnownHeaders.GetOrCreateHeaderName(name);
        var valueStr = WellKnownHeaders.GetOrCreateHeaderValue(value);

        // RFC 9112 §5.5: Header field values MUST NOT contain CR, LF, or NUL characters.
        if (valueStr.IndexOfAny('\r', '\n', '\0') >= 0)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidFieldValue,
                $"Header '{nameStr}' contains a CR, LF, or NUL character in its value.");
        }

        // Security: check single header field size (name + ": " + value).
        var headerSize = name.Length + 2 + value.Length;
        if (headerSize > _maxHeaderSize)
        {
            throw new HttpDecoderException(HttpDecoderError.HeaderTooLarge,
                $"Header '{nameStr}' is {headerSize} bytes; limit is {_maxHeaderSize}.");
        }

        // Security: check cumulative total header size.
        totalSize += headerSize;
        if (totalSize > _maxTotalHeaderSize)
        {
            throw new HttpDecoderException(HttpDecoderError.TotalHeadersTooLarge,
                $"Total header size is {totalSize} bytes; limit is {_maxTotalHeaderSize}.");
        }

        return (nameStr, valueStr);
    }
}
