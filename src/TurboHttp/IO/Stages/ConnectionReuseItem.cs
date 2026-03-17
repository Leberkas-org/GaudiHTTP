using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.IO.Stages;

public record ConnectionReuseItem(RequestEndpoint Key, ConnectionReuseDecision Decision) : IControlItem;