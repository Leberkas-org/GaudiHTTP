using System.Net;

namespace TurboHTTP.Protocol.Syntax.Http11;

internal static class Http11Profile
{
    public const bool SupportsChunked = true;
    public const bool DefaultPersistent = true;
    public const bool RequiresHost = true;
    public static readonly Version Version = HttpVersion.Version11;
}
