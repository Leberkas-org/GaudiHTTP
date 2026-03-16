using System;

namespace TurboHttp.IO.Stages;

public record ConnectItem(TcpOptions Options, Version HttpVersion) : IControlItem
{
    public HostKey Key { get; } = new()
    {
        Host = Options.Host,
        Port = (ushort)Options.Port,
        Scheme = Options is TlsOptions ? "https" : "http",
        Version = HttpVersion
    };
}