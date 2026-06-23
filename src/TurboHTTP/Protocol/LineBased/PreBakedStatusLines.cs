using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class PreBakedStatusLines
{
    private static readonly Dictionary<int, byte[]> Http11Lines = Build("HTTP/1.1"u8);
    private static readonly Dictionary<int, byte[]> Http10Lines = Build("HTTP/1.0"u8);

    public static bool TryGetHttp11(int statusCode, out ReadOnlyMemory<byte> line)
    {
        if (Http11Lines.TryGetValue(statusCode, out var bytes))
        {
            line = bytes;
            return true;
        }

        line = default;
        return false;
    }

    public static bool TryGetHttp10(int statusCode, out ReadOnlyMemory<byte> line)
    {
        if (Http10Lines.TryGetValue(statusCode, out var bytes))
        {
            line = bytes;
            return true;
        }

        line = default;
        return false;
    }

    private static Dictionary<int, byte[]> Build(ReadOnlySpan<byte> versionPrefix)
    {
        var codes = new int[]
        {
            100, 101,
            200, 201, 202, 204, 206,
            301, 302, 303, 304, 307, 308,
            400, 401, 403, 404, 405, 408, 409, 411, 413, 414, 415, 416, 417, 426,
            500, 501, 502, 503, 504, 505
        };

        var table = new Dictionary<int, byte[]>(codes.Length);

        foreach (var code in codes)
        {
            var phrase = ReasonPhrases.ForBytes(code);
            // "HTTP/1.x NNN phrase\r\n"
            var totalLength = versionPrefix.Length + 1 + 3 + 1 + phrase.Length + 2;
            var buffer = new byte[totalLength];

            var pos = 0;
            versionPrefix.CopyTo(buffer.AsSpan(pos));
            pos += versionPrefix.Length;
            buffer[pos++] = (byte)' ';
            buffer[pos++] = (byte)('0' + code / 100);
            buffer[pos++] = (byte)('0' + code / 10 % 10);
            buffer[pos++] = (byte)('0' + code % 10);
            buffer[pos++] = (byte)' ';
            phrase.CopyTo(buffer.AsSpan(pos));
            pos += phrase.Length;
            buffer[pos++] = (byte)'\r';
            buffer[pos] = (byte)'\n';

            table[code] = buffer;
        }

        return table;
    }
}
