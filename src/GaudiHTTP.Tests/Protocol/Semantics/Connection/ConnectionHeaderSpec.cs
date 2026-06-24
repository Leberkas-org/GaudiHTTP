using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.Connection;

public sealed class ConnectionHeaderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_parse_single_option()
    {
        const string headerValue = "close";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Single(options);
        Assert.Contains("close", options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_parse_multiple_options()
    {
        const string headerValue = "close, upgrade, keep-alive";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Equal(3, options.Count);
        Assert.Contains("close", options);
        Assert.Contains("upgrade", options);
        Assert.Contains("keep-alive", options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_be_case_insensitive()
    {
        const string headerValue = "CLOSE, Upgrade, Keep-Alive";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Equal(3, options.Count);
        var normalized = options.Select(o => o.ToLowerInvariant()).ToHashSet();
        Assert.Contains("close", normalized);
        Assert.Contains("upgrade", normalized);
        Assert.Contains("keep-alive", normalized);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_handle_whitespace_around_options()
    {
        const string headerValue = "  close  ,  upgrade  ";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Equal(2, options.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_recognize_close_option()
    {
        const string headerValue = "close";
        var hasClose = ConnectionHeaderSemantics.HasCloseOption(headerValue);

        Assert.True(hasClose);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_recognize_upgrade_option()
    {
        const string headerValue = "upgrade";
        var hasUpgrade = ConnectionHeaderSemantics.HasUpgradeOption(headerValue);

        Assert.True(hasUpgrade);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_not_find_close_when_absent()
    {
        const string headerValue = "upgrade";
        var hasClose = ConnectionHeaderSemantics.HasCloseOption(headerValue);

        Assert.False(hasClose);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_not_find_upgrade_when_absent()
    {
        const string headerValue = "close";
        var hasUpgrade = ConnectionHeaderSemantics.HasUpgradeOption(headerValue);

        Assert.False(hasUpgrade);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_find_close_case_insensitively()
    {
        const string headerValue = "Close";
        var hasClose = ConnectionHeaderSemantics.HasCloseOption(headerValue);

        Assert.True(hasClose);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_find_upgrade_case_insensitively()
    {
        const string headerValue = "UPGRADE, Close";
        var hasUpgrade = ConnectionHeaderSemantics.HasUpgradeOption(headerValue);

        Assert.True(hasUpgrade);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_handle_empty_header()
    {
        const string headerValue = "";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Empty(options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_handle_null_header()
    {
        var options = ConnectionHeaderSemantics.Parse(null);

        Assert.NotNull(options);
        Assert.Empty(options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_handle_whitespace_only_header()
    {
        const string headerValue = "   ";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Empty(options);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_not_recognize_invalid_tokens()
    {
        const string headerValue = "close, invalid:token, upgrade";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.DoesNotContain("invalid:token", options);
    }

    [Theory(Timeout = 5000)]
    [InlineData("close")]
    [InlineData("Close")]
    [InlineData("CLOSE")]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_recognize_close_with_case_variation(string value)
    {
        var hasClose = ConnectionHeaderSemantics.HasCloseOption(value);

        Assert.True(hasClose);
    }

    [Theory(Timeout = 5000)]
    [InlineData("upgrade")]
    [InlineData("Upgrade")]
    [InlineData("UPGRADE")]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_recognize_upgrade_with_case_variation(string value)
    {
        var hasUpgrade = ConnectionHeaderSemantics.HasUpgradeOption(value);

        Assert.True(hasUpgrade);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_allow_te_as_option()
    {
        const string headerValue = "TE";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Contains("te", options, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void ConnectionHeader_should_allow_keep_alive_as_option()
    {
        const string headerValue = "Keep-Alive";
        var options = ConnectionHeaderSemantics.Parse(headerValue);

        Assert.NotNull(options);
        Assert.Contains("keep-alive", options, StringComparer.OrdinalIgnoreCase);
    }
}
