using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace Servus.Akka.Transport;

public sealed record ConnectionInfo(
    EndPoint Local,
    EndPoint Remote,
    SslProtocols? NegotiatedSslProtocol,
    SslApplicationProtocol? NegotiatedApplicationProtocol);
