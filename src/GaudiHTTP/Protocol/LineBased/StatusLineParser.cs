using System.Text;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.LineBased;

internal static class StatusLineParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> data,
        out Version version,
        out int statusCode,
        out string reasonPhrase,
        out int consumed)
    {
        version = null!;
        statusCode = 0;
        reasonPhrase = null!;
        consumed = 0;

        var crlf = BufferSearch.FindCrlf(data, 0);
        if (crlf < 0)
        {
            return false;
        }

        var line = data[..crlf];
        var firstSpace = BufferSearch.FindSpace(line, 0);
        if (firstSpace <= 0)
        {
            return false;
        }

        var versionSpan = line[..firstSpace];

        if (!versionSpan.StartsWith(WellKnownHeaders.Http))
        {
            throw new ArgumentException($"Invalid HTTP version string: '{Encoding.ASCII.GetString(versionSpan)}'.");
        }

        if (!MessageVersionCodec.TryParse(versionSpan, out version))
        {
            return false;
        }

        var secondSpace = BufferSearch.FindSpace(line, firstSpace + 1);
        if (secondSpace <= firstSpace + 1)
        {
            return false;
        }

        var codeSlice = line[(firstSpace + 1)..secondSpace];
        if (codeSlice.Length != 3)
        {
            return false;
        }

        if (!IsAsciiDigit(codeSlice[0]) || !IsAsciiDigit(codeSlice[1]) || !IsAsciiDigit(codeSlice[2]))
        {
            return false;
        }

        statusCode = (codeSlice[0] - '0') * 100 + (codeSlice[1] - '0') * 10 + (codeSlice[2] - '0');

        if (statusCode is < 100 or > 599)
        {
            return false;
        }

        reasonPhrase = secondSpace + 1 < line.Length
            ? ReasonPhrases.ResolveCached(statusCode, line[(secondSpace + 1)..])
            : string.Empty;

        consumed = crlf + 2;
        return true;
    }

    private static bool IsAsciiDigit(byte b) => b >= '0' && b <= '9';
}