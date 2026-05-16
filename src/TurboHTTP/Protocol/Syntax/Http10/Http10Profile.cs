using System.Net;

namespace TurboHTTP.Protocol.Syntax.Http10;

internal sealed record Http10Profile
{
    public Version Version { get; init; } = HttpVersion.Version10;
    public bool SupportsChunked { get; init; } = false;
    public bool DefaultPersistent { get; init; } = false;
    public bool RequiresHost { get; init; } = false;

    public static Http10Profile Default { get; } = new();
}
