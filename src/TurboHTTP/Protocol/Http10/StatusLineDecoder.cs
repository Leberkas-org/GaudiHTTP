namespace TurboHTTP.Protocol.Http10;

internal static class StatusLineDecoder
{
    internal static void Validate(string statusLine)
    {
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var code))
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidStatusLine, $"Line: '{statusLine}'.");
        }

        if (code is < 100 or > 999)
        {
            throw new HttpDecoderException(HttpDecoderError.InvalidStatusLine,
                $"Status code {code} is out of the valid range 100–999.");
        }
    }

    internal static int ParseCode(string statusLine)
    {
        var parts = statusLine.Split(' ', 3);
        return parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : 500;
    }
}
