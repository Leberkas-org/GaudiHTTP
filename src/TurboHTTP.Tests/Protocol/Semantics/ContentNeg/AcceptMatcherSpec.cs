using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.ContentNeg;

public sealed class AcceptMatcherSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.1")]
    public void AcceptMatcher_should_match_exact_media_type()
    {
        Assert.True(AcceptMatcher.MatchesMediaType("text/html", "text/html"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.1")]
    public void AcceptMatcher_should_not_match_different_media_type()
    {
        Assert.False(AcceptMatcher.MatchesMediaType("text/html", "application/json"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.1")]
    public void AcceptMatcher_should_match_media_type_wildcard()
    {
        Assert.True(AcceptMatcher.MatchesMediaType("*/*", "text/html"));
        Assert.True(AcceptMatcher.MatchesMediaType("*/*", "application/json"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.1")]
    public void AcceptMatcher_should_match_type_wildcard()
    {
        Assert.True(AcceptMatcher.MatchesMediaType("text/*", "text/html"));
        Assert.True(AcceptMatcher.MatchesMediaType("text/*", "text/plain"));
        Assert.False(AcceptMatcher.MatchesMediaType("text/*", "application/json"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.3")]
    public void AcceptMatcher_should_match_encoding()
    {
        Assert.True(AcceptMatcher.MatchesEncoding("gzip", "gzip"));
        Assert.True(AcceptMatcher.MatchesEncoding("deflate", "deflate"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.3")]
    public void AcceptMatcher_should_always_accept_identity_encoding()
    {
        Assert.True(AcceptMatcher.MatchesEncoding("identity", "gzip"));
        Assert.True(AcceptMatcher.MatchesEncoding("identity", "deflate"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.3")]
    public void AcceptMatcher_should_match_encoding_wildcard()
    {
        Assert.True(AcceptMatcher.MatchesEncoding("*", "gzip"));
        Assert.True(AcceptMatcher.MatchesEncoding("*", "deflate"));
        Assert.True(AcceptMatcher.MatchesEncoding("*", "br"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.4")]
    public void AcceptMatcher_should_match_language_prefix()
    {
        Assert.True(AcceptMatcher.MatchesLanguage("en", "en-US"));
        Assert.True(AcceptMatcher.MatchesLanguage("en", "en-GB"));
        Assert.True(AcceptMatcher.MatchesLanguage("fr", "fr-CA"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.4")]
    public void AcceptMatcher_should_match_language_wildcard()
    {
        Assert.True(AcceptMatcher.MatchesLanguage("*", "en-US"));
        Assert.True(AcceptMatcher.MatchesLanguage("*", "fr-CA"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.1")]
    public void AcceptMatcher_should_accept_all_when_null_pattern()
    {
        Assert.True(AcceptMatcher.MatchesMediaType(null, "text/html"));
        Assert.True(AcceptMatcher.MatchesMediaType("", "application/json"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.1")]
    public void AcceptMatcher_should_be_case_insensitive()
    {
        Assert.True(AcceptMatcher.MatchesMediaType("TEXT/HTML", "text/html"));
        Assert.True(AcceptMatcher.MatchesMediaType("text/HTML", "TEXT/html"));
        Assert.True(AcceptMatcher.MatchesEncoding("GZIP", "gzip"));
        Assert.True(AcceptMatcher.MatchesLanguage("EN", "en-us"));
    }
}
