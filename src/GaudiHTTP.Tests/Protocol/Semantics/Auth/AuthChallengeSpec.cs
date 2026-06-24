using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Auth;

public sealed class AuthChallengeSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.1")]
    public void AuthChallenge_should_parse_case_insensitive_scheme()
    {
        var challenge = AuthChallenge.Parse("Bearer token123");

        Assert.Equal("bearer", challenge.Scheme);
        Assert.Equal("token123", challenge.Token68);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.3")]
    public void AuthChallenge_should_parse_scheme_with_realm()
    {
        var challenge = AuthChallenge.Parse("Basic realm=\"example.com\"");

        Assert.Equal("basic", challenge.Scheme);
        Assert.Equal("example.com", challenge.Realm);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.4")]
    public void AuthChallenge_should_parse_token68_format()
    {
        var challenge = AuthChallenge.Parse("Bearer eyJhbGciOiJIUzI1NiJ9");

        Assert.Equal("bearer", challenge.Scheme);
        Assert.Equal("eyJhbGciOiJIUzI1NiJ9", challenge.Token68);
        Assert.Null(challenge.Realm);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.5")]
    public void AuthChallenge_should_format_credentials()
    {
        var formatted = AuthChallenge.FormatCredentials("Bearer", "token123");

        Assert.Equal("Bearer token123", formatted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.5")]
    public void AuthChallenge_should_parse_multiple_parameters()
    {
        var challenge = AuthChallenge.Parse("Digest realm=\"api\", algorithm=MD5, nonce=\"abc123\"");

        Assert.Equal("digest", challenge.Scheme);
        Assert.Equal("api", challenge.Realm);
        Assert.True(challenge.Parameters.ContainsKey("algorithm"));
        Assert.True(challenge.Parameters.ContainsKey("nonce"));
        Assert.Equal("MD5", challenge.Parameters["algorithm"]);
        Assert.Equal("abc123", challenge.Parameters["nonce"]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.6.1")]
    public void AuthChallenge_should_parse_multiple_challenges()
    {
        var challenges = AuthChallenge.ParseList("Basic realm=\"site1\", Bearer realm=\"api\"");

        Assert.Equal(2, challenges.Count);
        Assert.Equal("basic", challenges[0].Scheme);
        Assert.Equal("bearer", challenges[1].Scheme);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.1")]
    public void AuthChallenge_should_handle_401_status_scenario()
    {
        var challenge = AuthChallenge.Parse("Basic realm=\"Protected Area\"");

        Assert.Equal("basic", challenge.Scheme);
        Assert.Equal("Protected Area", challenge.Realm);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.7.1")]
    public void AuthChallenge_should_handle_407_status_proxy_auth()
    {
        var challenge = AuthChallenge.Parse("Bearer");

        Assert.Equal("bearer", challenge.Scheme);
        Assert.Null(challenge.Token68);
        Assert.Null(challenge.Realm);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.5")]
    public void AuthChallenge_should_scope_realm_to_scheme()
    {
        var challenges = AuthChallenge.ParseList("Basic realm=\"users\", Bearer realm=\"api\"");

        Assert.Equal("users", challenges[0].Realm);
        Assert.Equal("api", challenges[1].Realm);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-11.1")]
    public void AuthChallenge_should_parse_scheme_only()
    {
        var challenge = AuthChallenge.Parse("Bearer");

        Assert.Equal("bearer", challenge.Scheme);
        Assert.Null(challenge.Token68);
        Assert.Null(challenge.Realm);
    }
}
