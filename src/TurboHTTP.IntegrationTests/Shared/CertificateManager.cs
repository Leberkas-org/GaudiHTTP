using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TurboHTTP.IntegrationTests.Shared;

internal static class CertificateManager
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "turbohttp-nginx-ssl");

    public static string SslDir => Path.Combine(TempDir, "ssl");

    public static void EnsureCertificatesExist()
    {
        if (Directory.Exists(TempDir) &&
            File.Exists(Path.Combine(SslDir, "cert.pem")) &&
            File.Exists(Path.Combine(SslDir, "key.pem")))
        {
            return;
        }

        Directory.CreateDirectory(SslDir);

        var (certPem, keyPem) = GenerateSelfSignedCert();
        File.WriteAllText(Path.Combine(SslDir, "cert.pem"), certPem);
        File.WriteAllText(Path.Combine(SslDir, "key.pem"), keyPem);
    }

    private static (string CertPem, string KeyPem) GenerateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));

        return (cert.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }
}