using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.Conditional;

public sealed class ETagComparerSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.2")]
    public void StrongMatch_Should_Match_Identical_Strong_ETags()
    {
        var result = ETagComparer.StrongMatch("\"abc123\"", "\"abc123\"");
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.2")]
    public void StrongMatch_Should_Not_Match_Different_ETags()
    {
        var result = ETagComparer.StrongMatch("\"abc123\"", "\"def456\"");
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.2")]
    public void StrongMatch_Should_Reject_Weak_ETags()
    {
        var result = ETagComparer.StrongMatch("W/\"abc123\"", "\"abc123\"");
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.1")]
    public void WeakMatch_Should_Match_Identical_ETags_Regardless_Of_Weakness()
    {
        var result = ETagComparer.WeakMatch("\"abc123\"", "\"abc123\"");
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.1")]
    public void WeakMatch_Should_Ignore_W_Prefix()
    {
        var result = ETagComparer.WeakMatch("W/\"abc123\"", "\"abc123\"");
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.1")]
    public void WeakMatch_Should_Match_Both_Weak_ETags()
    {
        var result = ETagComparer.WeakMatch("W/\"abc123\"", "W/\"abc123\"");
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.1")]
    public void WeakMatch_Should_Not_Match_Different_ETags()
    {
        var result = ETagComparer.WeakMatch("\"abc123\"", "\"def456\"");
        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.2")]
    public void StrongMatch_Should_Match_Star_Against_Any_ETag()
    {
        var result1 = ETagComparer.StrongMatch("*", "\"abc123\"");
        var result2 = ETagComparer.StrongMatch("\"abc123\"", "*");
        var result3 = ETagComparer.StrongMatch("*", "*");

        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.8.3.1")]
    public void WeakMatch_Should_Match_Star_Against_Any_ETag()
    {
        var result1 = ETagComparer.WeakMatch("*", "\"abc123\"");
        var result2 = ETagComparer.WeakMatch("\"abc123\"", "*");
        var result3 = ETagComparer.WeakMatch("*", "W/\"abc123\"");

        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
    }
}
