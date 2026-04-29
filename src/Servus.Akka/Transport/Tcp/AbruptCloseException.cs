namespace Servus.Akka.Transport.Tcp;

internal sealed class AbruptCloseException() : Exception("Connection closed abruptly.");
