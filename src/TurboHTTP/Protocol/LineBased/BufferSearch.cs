namespace TurboHTTP.Protocol.LineBased;

internal static class BufferSearch
{
    public static int FindCrlf(ReadOnlySpan<byte> data, int start)
    {
        if (start >= data.Length)
        {
            return -1;
        }

        var slice = data[start..];
        var offset = 0;
        while (offset < slice.Length)
        {
            var cr = slice[offset..].IndexOf((byte)'\r');
            if (cr < 0)
            {
                return -1;
            }

            var idx = offset + cr;
            if (idx + 1 < slice.Length && slice[idx + 1] == (byte)'\n')
            {
                return start + idx;
            }

            offset = idx + 1;
        }

        return -1;
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
