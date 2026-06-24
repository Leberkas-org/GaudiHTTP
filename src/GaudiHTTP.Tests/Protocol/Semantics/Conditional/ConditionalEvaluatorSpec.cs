using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Conditional;

public sealed class ConditionalEvaluatorSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.1")]
    public void Should_Evaluate_IfMatch_Success()
    {
        var result = ConditionalEvaluator.Evaluate(
            ifMatch: "\"abc123\"",
            currentETag: "\"abc123\"");

        Assert.Equal(PreconditionResult.Continue, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.1")]
    public void Should_Evaluate_IfMatch_Failure()
    {
        var result = ConditionalEvaluator.Evaluate(
            ifMatch: "\"abc123\"",
            currentETag: "\"def456\"");

        Assert.Equal(PreconditionResult.PreconditionFailed, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.1")]
    public void Should_Evaluate_IfMatch_Star()
    {
        var result = ConditionalEvaluator.Evaluate(
            ifMatch: "*",
            currentETag: "\"abc123\"");

        Assert.Equal(PreconditionResult.Continue, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.2")]
    public void Should_Evaluate_IfNoneMatch_Success_On_Get()
    {
        var result = ConditionalEvaluator.Evaluate(
            ifNoneMatch: "\"abc123\"",
            currentETag: "\"abc123\"",
            methodIsGetOrHead: true);

        Assert.Equal(PreconditionResult.NotModified, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.2")]
    public void Should_Evaluate_IfNoneMatch_Failure_On_NonGet()
    {
        var result = ConditionalEvaluator.Evaluate(
            ifNoneMatch: "\"abc123\"",
            currentETag: "\"abc123\"",
            methodIsGetOrHead: false);

        Assert.Equal(PreconditionResult.PreconditionFailed, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.2")]
    public void Should_Evaluate_IfNoneMatch_Star()
    {
        var result = ConditionalEvaluator.Evaluate(
            ifNoneMatch: "*",
            currentETag: "\"abc123\"",
            methodIsGetOrHead: true);

        Assert.Equal(PreconditionResult.NotModified, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.3")]
    public void Should_Evaluate_IfModifiedSince_NotModified()
    {
        var lastModified = DateTimeOffset.Parse("2024-01-01T10:00:00Z");
        var ifModifiedSince = DateTimeOffset.Parse("2024-01-01T12:00:00Z");

        var result = ConditionalEvaluator.Evaluate(
            ifModifiedSince: ifModifiedSince,
            lastModified: lastModified,
            methodIsGetOrHead: true);

        Assert.Equal(PreconditionResult.NotModified, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.3")]
    public void Should_Evaluate_IfModifiedSince_Continue()
    {
        var lastModified = DateTimeOffset.Parse("2024-01-01T14:00:00Z");
        var ifModifiedSince = DateTimeOffset.Parse("2024-01-01T12:00:00Z");

        var result = ConditionalEvaluator.Evaluate(
            ifModifiedSince: ifModifiedSince,
            lastModified: lastModified,
            methodIsGetOrHead: true);

        Assert.Equal(PreconditionResult.Continue, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.4")]
    public void Should_Evaluate_IfUnmodifiedSince_Failure()
    {
        var lastModified = DateTimeOffset.Parse("2024-01-01T14:00:00Z");
        var ifUnmodifiedSince = DateTimeOffset.Parse("2024-01-01T12:00:00Z");

        var result = ConditionalEvaluator.Evaluate(
            ifUnmodifiedSince: ifUnmodifiedSince,
            lastModified: lastModified);

        Assert.Equal(PreconditionResult.PreconditionFailed, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.1.4")]
    public void Should_Evaluate_IfUnmodifiedSince_Continue()
    {
        var lastModified = DateTimeOffset.Parse("2024-01-01T10:00:00Z");
        var ifUnmodifiedSince = DateTimeOffset.Parse("2024-01-01T12:00:00Z");

        var result = ConditionalEvaluator.Evaluate(
            ifUnmodifiedSince: ifUnmodifiedSince,
            lastModified: lastModified);

        Assert.Equal(PreconditionResult.Continue, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.2")]
    public void Should_Evaluate_IfMatch_Before_IfNoneMatch()
    {
        var lastModified = DateTimeOffset.Parse("2024-01-01T10:00:00Z");

        // If-Match succeeds, so If-None-Match is not evaluated (they don't apply together).
        var result = ConditionalEvaluator.Evaluate(
            ifMatch: "\"abc123\"",
            ifNoneMatch: "\"def456\"",
            currentETag: "\"abc123\"",
            methodIsGetOrHead: true);

        Assert.Equal(PreconditionResult.Continue, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-13.2")]
    public void Should_Evaluate_No_Conditions()
    {
        var result = ConditionalEvaluator.Evaluate();

        Assert.Equal(PreconditionResult.Continue, result);
    }
}
