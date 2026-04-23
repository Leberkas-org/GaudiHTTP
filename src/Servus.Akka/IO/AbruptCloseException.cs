namespace Servus.Akka.IO;

public sealed class AbruptCloseException()
    : Exception("Connection closed abruptly without close_notify");