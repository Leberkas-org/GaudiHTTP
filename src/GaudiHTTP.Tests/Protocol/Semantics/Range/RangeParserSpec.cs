using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.Range;

public sealed class RangeParserSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Parse_Single_Range()
    {
        var ranges = RangeParser.Parse("bytes=0-499");

        Assert.Single(ranges);
        Assert.Equal(0, ranges[0].Start);
        Assert.Equal(499, ranges[0].End);
        Assert.Null(ranges[0].SuffixLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Parse_Suffix_Range()
    {
        var ranges = RangeParser.Parse("bytes=-500");

        Assert.Single(ranges);
        Assert.Null(ranges[0].Start);
        Assert.Null(ranges[0].End);
        Assert.Equal(500, ranges[0].SuffixLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Parse_OpenEnded_Range()
    {
        var ranges = RangeParser.Parse("bytes=500-");

        Assert.Single(ranges);
        Assert.Equal(500, ranges[0].Start);
        Assert.Null(ranges[0].End);
        Assert.Null(ranges[0].SuffixLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Parse_Multiple_Ranges()
    {
        var ranges = RangeParser.Parse("bytes=0-499,500-999,1000-1499");

        Assert.Equal(3, ranges.Count);
        Assert.Equal(0, ranges[0].Start);
        Assert.Equal(499, ranges[0].End);
        Assert.Equal(500, ranges[1].Start);
        Assert.Equal(999, ranges[1].End);
        Assert.Equal(1000, ranges[2].Start);
        Assert.Equal(1499, ranges[2].End);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Return_Empty_For_Invalid_Syntax()
    {
        var ranges1 = RangeParser.Parse("bytes=");
        var ranges2 = RangeParser.Parse("bytes=500-100");

        Assert.Empty(ranges1);
        Assert.Empty(ranges2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Return_Empty_For_First_GreaterThan_Last()
    {
        var ranges = RangeParser.Parse("bytes=1000-500");

        Assert.Empty(ranges);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Return_Empty_For_Non_Bytes_Range()
    {
        var ranges = RangeParser.Parse("words=0-499");

        Assert.Empty(ranges);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Handle_Whitespace_Around_Commas()
    {
        var ranges = RangeParser.Parse("bytes=0-499 , 500-999");

        Assert.Equal(2, ranges.Count);
        Assert.Equal(0, ranges[0].Start);
        Assert.Equal(499, ranges[0].End);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1")]
    public void Should_Return_Empty_For_Null_Input()
    {
        var ranges = RangeParser.Parse(null);

        Assert.Empty(ranges);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.4")]
    public void Should_Parse_ContentRange_ByteRange()
    {
        var contentRange = RangeParser.ParseContentRange("bytes 0-499/1000");

        Assert.NotNull(contentRange);
        Assert.Equal(0, contentRange.Value.Start);
        Assert.Equal(499, contentRange.Value.End);
        Assert.Equal(1000, contentRange.Value.CompleteLength);
        Assert.False(contentRange.Value.IsUnsatisfied);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.4")]
    public void Should_Parse_ContentRange_Unsatisfied()
    {
        var contentRange = RangeParser.ParseContentRange("bytes */1000");

        Assert.NotNull(contentRange);
        Assert.Null(contentRange.Value.Start);
        Assert.Null(contentRange.Value.End);
        Assert.Equal(1000, contentRange.Value.CompleteLength);
        Assert.True(contentRange.Value.IsUnsatisfied);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.4")]
    public void Should_Parse_ContentRange_UnknownTotal()
    {
        var contentRange = RangeParser.ParseContentRange("bytes 0-499/*");

        Assert.NotNull(contentRange);
        Assert.Equal(0, contentRange.Value.Start);
        Assert.Equal(499, contentRange.Value.End);
        Assert.Null(contentRange.Value.CompleteLength);
        Assert.False(contentRange.Value.IsUnsatisfied);
    }
}
