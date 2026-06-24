using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.LineBased;

internal enum HeaderBlockResult
{
    NeedMore,
    Complete
}

internal sealed class HeaderBlockReader(int maxHeaderBytes, int maxHeaderCount, int maxLineLength, bool allowObsFold)
{
    private readonly HeaderCollection _headers = new();
    private int _totalBytes;
    private int _headerCount;

    public HeaderCollection GetHeaders() => _headers;

    public void Reset()
    {
        _headers.Clear();
        _totalBytes = 0;
        _headerCount = 0;
    }

    public HeaderBlockResult Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        var pos = 0;
        while (true)
        {
            var crlf = BufferSearch.FindCrlf(data, pos);
            if (crlf < 0)
            {
                consumed = pos;
                return HeaderBlockResult.NeedMore;
            }

            var lineLen = crlf - pos;
            if (lineLen == 0)
            {
                consumed = crlf + 2;
                return HeaderBlockResult.Complete;
            }

            if (lineLen > maxLineLength)
            {
                throw new HttpProtocolException($"Header line exceeds {maxLineLength} bytes.");
            }

            _totalBytes += lineLen + 2;
            if (_totalBytes > maxHeaderBytes)
            {
                throw new HttpProtocolException($"Header block exceeds {maxHeaderBytes} bytes.");
            }

            var line = data.Slice(pos, lineLen);

            if (line[0] == (byte)' ' || line[0] == (byte)'\t')
            {
                if (!allowObsFold)
                {
                    throw new HttpProtocolException("obs-fold not permitted in header block.");
                }

                pos = crlf + 2;
                continue;
            }

            _headerCount++;
            if (_headerCount > maxHeaderCount)
            {
                throw new HttpProtocolException($"Header count exceeds {maxHeaderCount}.");
            }

            if (!HeaderFieldParser.TryParse(line, out var name, out var value))
            {
                throw new HttpProtocolException("Malformed header field.");
            }

            _headers.Add(name, value);
            pos = crlf + 2;
        }
    }
}
