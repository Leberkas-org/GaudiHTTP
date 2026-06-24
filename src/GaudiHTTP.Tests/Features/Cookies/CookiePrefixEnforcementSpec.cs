using GaudiHTTP.Features.Cookies;

namespace GaudiHTTP.Tests.Features.Cookies;

/// <summary>
/// RFC 6265bis §4.1.3 — Cookie name prefix enforcement.
/// __Host- cookies must have Secure, no Domain, and Path="/".
/// __Secure- cookies must have Secure.
/// </summary>
public sealed class CookiePrefixEnforcementSpec
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static Uri Uri(string url) => new(url);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Host_prefix_should_be_accepted_when_all_rules_met()
    {
        var entry = CookieParser.Parse(
            "__Host-id=abc; Secure; Path=/",
            Uri("https://example.com/"), Now);

        Assert.NotNull(entry);
        Assert.Equal("__Host-id", entry.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Host_prefix_should_be_rejected_when_not_secure()
    {
        var entry = CookieParser.Parse(
            "__Host-id=abc; Path=/",
            Uri("https://example.com/"), Now);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Host_prefix_should_be_rejected_when_domain_present()
    {
        var entry = CookieParser.Parse(
            "__Host-id=abc; Secure; Path=/; Domain=example.com",
            Uri("https://example.com/"), Now);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Host_prefix_should_be_rejected_when_path_not_root()
    {
        var entry = CookieParser.Parse(
            "__Host-id=abc; Secure; Path=/foo",
            Uri("https://example.com/"), Now);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Host_prefix_should_be_rejected_when_path_absent()
    {
        var entry = CookieParser.Parse(
            "__Host-id=abc; Secure",
            Uri("https://example.com/path"), Now);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Secure_prefix_should_be_accepted_when_secure()
    {
        var entry = CookieParser.Parse(
            "__Secure-tok=xyz; Secure",
            Uri("https://example.com/"), Now);

        Assert.NotNull(entry);
        Assert.Equal("__Secure-tok", entry.Name);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Secure_prefix_should_be_rejected_when_not_secure()
    {
        var entry = CookieParser.Parse(
            "__Secure-tok=xyz",
            Uri("https://example.com/"), Now);

        Assert.Null(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Host_prefix_check_should_be_case_insensitive()
    {
        var entry = CookieParser.Parse(
            "__HOST-id=abc; Secure; Path=/",
            Uri("https://example.com/"), Now);

        Assert.NotNull(entry);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC6265bis-4.1.3")]
    public void Secure_prefix_check_should_be_case_insensitive()
    {
        var entry = CookieParser.Parse(
            "__SECURE-tok=xyz; Secure",
            Uri("https://example.com/"), Now);

        Assert.NotNull(entry);
    }
}
