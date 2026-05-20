using System.Net.Security;

namespace Servus.Akka.Transport;

public delegate ValueTask<SslServerAuthenticationOptions> TlsHandshakeCallback(TlsHandshakeContext context);
