namespace TurboHTTP.IntegrationTests.Shared;

public sealed record ProtocolVariant(TestHttpVersion Version, bool Tls)
{
    public override string ToString() => Tls ? $"{Version}/TLS" : Version.ToString();
}