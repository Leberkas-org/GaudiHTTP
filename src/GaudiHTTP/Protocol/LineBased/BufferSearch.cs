namespace GaudiHTTP.Protocol.LineBased;

internal static class BufferSearch
{
    public static int FindCrlf(ReadOnlySpan<byte> data, int start)
    {
        if (start >= data.Length)
        {
            return -1;
        }

        // Single vectorized two-byte search instead of "find CR, then check the next byte and
        // restart on a lone CR". Same semantics: the first CRLF pair at or after start (a lone CR
        // is never matched; CR-CR-LF matches the second CR), with no scan restarts.
        var idx = data[start..].IndexOf("\r\n"u8);
        return idx < 0 ? -1 : start + idx;
    }

    public static int FindCrlfCrlf(ReadOnlySpan<byte> data, int start)
    {
        var pos = start;
        while (true)
        {
            var crlf = FindCrlf(data, pos);
            if (crlf < 0)
            {
                return -1;
            }

            if (crlf + 2 < data.Length - 1
                && data[crlf + 2] == (byte)'\r'
                && data[crlf + 3] == (byte)'\n')
            {
                return crlf;
            }

            pos = crlf + 2;
        }
    }

    public static int FindSpace(ReadOnlySpan<byte> data, int start)
    {
        if (start >= data.Length)
        {
            return -1;
        }

        var idx = data[start..].IndexOf((byte)' ');
        return idx < 0 ? -1 : start + idx;
    }

    public static int SkipOws(ReadOnlySpan<byte> data, int start)
    {
        var i = start;
        while (i < data.Length && (data[i] == (byte)' ' || data[i] == (byte)'\t'))
        {
            i++;
        }

        return i;
    }
}
