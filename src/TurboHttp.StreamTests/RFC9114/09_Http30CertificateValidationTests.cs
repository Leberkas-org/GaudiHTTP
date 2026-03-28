using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests HTTP/3 certificate validation integration per RFC 9114 §3.3.
/// Verifies that <see cref="Http3CertificateValidator"/> correctly validates server certificates
/// for hostname coverage, as used by <c>QuicClientProvider</c> after QUIC handshake.
/// </summary>
/// <remarks>
/// Component under test: <see cref="Http3CertificateValidator"/>.
/// RFC 9114 §3.3: A client MUST NOT reuse a connection to an origin unless the server certificate
/// covers the target hostname. SAN dNSName entries take precedence over CN; wildcard matching
/// applies to the leftmost label only.
///
/// Note: Certificate validation is integrated at the transport level (<c>QuicClientProvider</c>),
/// not in a GraphStage. These tests verify the validator behavior that gates connection coalescing.
/// </remarks>
public sealed class Http30CertificateValidationTests : StreamTestBase
{
    private static X509Certificate2 CreateSelfSignedCert(string commonName, params string[] sanDnsNames)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (sanDnsNames.Length > 0)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var dns in sanDnsNames)
            {
                sanBuilder.AddDnsName(dns);
            }
            request.CertificateExtensions.Add(sanBuilder.Build());
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    }

    // ──────────────────────────────────────────────────────────────────────
    // SAN Hostname Match (RFC 9114 §3.3)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-3.3-CV-001: Certificate with matching SAN covers hostname")]
    public void Should_CoverHostname_When_SanMatchesExactly()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "example.com");

        Assert.True(Http3CertificateValidator.CoversHostname(cert, "example.com"));
    }

    [Fact(DisplayName = "RFC9114-3.3-CV-002: Certificate without matching SAN does not cover hostname")]
    public void Should_NotCoverHostname_When_SanDoesNotMatch()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "other.com");

        Assert.False(Http3CertificateValidator.CoversHostname(cert, "example.com"));
    }

    [Fact(DisplayName = "RFC9114-3.3-CV-003: Wildcard SAN matches subdomain")]
    public void Should_CoverSubdomain_When_WildcardSanPresent()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "*.example.com");

        Assert.True(Http3CertificateValidator.CoversHostname(cert, "api.example.com"));
    }

    [Fact(DisplayName = "RFC9114-3.3-CV-004: Wildcard SAN does not match bare domain")]
    public void Should_NotCoverBareDomain_When_WildcardSanPresent()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "*.example.com");

        Assert.False(Http3CertificateValidator.CoversHostname(cert, "example.com"));
    }

    [Fact(DisplayName = "RFC9114-3.3-CV-005: Wildcard SAN does not match nested subdomain")]
    public void Should_NotCoverNestedSubdomain_When_WildcardSanPresent()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "*.example.com");

        Assert.False(Http3CertificateValidator.CoversHostname(cert, "deep.sub.example.com"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // CN Fallback (RFC 9114 §3.3)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-3.3-CV-006: CN fallback covers hostname when no SAN present")]
    public void Should_CoverHostname_When_CnMatchesAndNoSan()
    {
        using var cert = CreateSelfSignedCert("example.com");

        Assert.True(Http3CertificateValidator.CoversHostname(cert, "example.com"));
    }

    [Fact(DisplayName = "RFC9114-3.3-CV-007: CN fallback does not cover mismatched hostname")]
    public void Should_NotCoverHostname_When_CnDoesNotMatch()
    {
        using var cert = CreateSelfSignedCert("other.com");

        Assert.False(Http3CertificateValidator.CoversHostname(cert, "example.com"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Multiple SAN Entries (RFC 9114 §3.3)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-3.3-CV-008: Multiple SAN entries — any match is sufficient")]
    public void Should_CoverHostname_When_AnySanMatches()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "alpha.com", "beta.com", "example.com");

        Assert.True(Http3CertificateValidator.CoversHostname(cert, "example.com"));
    }

    [Fact(DisplayName = "RFC9114-3.3-CV-009: Multiple SAN entries — none match")]
    public void Should_NotCoverHostname_When_NoSanMatches()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "alpha.com", "beta.com");

        Assert.False(Http3CertificateValidator.CoversHostname(cert, "example.com"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Connection Reuse Evaluator Integration (RFC 9114 §3.3)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-3.3-CV-010: Reuse evaluator denies cross-origin when cert lacks coverage")]
    public void Should_DenyReuse_When_CertDoesNotCoverTargetHost()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "origin.com");

        var decision = Http3ConnectionReuseEvaluator.Evaluate(
            connectionScheme: "https",
            connectionHost: "origin.com",
            connectionPort: 443,
            targetScheme: "https",
            targetHost: "other.com",
            targetPort: 443,
            serverCertificate: cert,
            isGoingAway: false);

        Assert.False(decision.CanReuse);
    }

    [Fact(DisplayName = "RFC9114-3.3-CV-011: Reuse evaluator allows cross-origin when cert covers target")]
    public void Should_AllowReuse_When_CertCoversTargetHost()
    {
        using var cert = CreateSelfSignedCert("Issuer CA", "origin.com", "other.com");

        var decision = Http3ConnectionReuseEvaluator.Evaluate(
            connectionScheme: "https",
            connectionHost: "origin.com",
            connectionPort: 443,
            targetScheme: "https",
            targetHost: "other.com",
            targetPort: 443,
            serverCertificate: cert,
            isGoingAway: false);

        Assert.True(decision.CanReuse);
    }
}
