using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.ContentNeg;

public sealed class QualityValueSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_default_quality_to_one()
    {
        var qv = QualityValue.Parse("text/html");

        Assert.Equal("text/html", qv.Value);
        Assert.Equal(1.0, qv.Quality);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_parse_explicit_quality()
    {
        var qv = QualityValue.Parse("text/html;q=0.5");

        Assert.Equal("text/html", qv.Value);
        Assert.Equal(0.5, qv.Quality);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_handle_quality_with_spaces()
    {
        var qv = QualityValue.Parse("text/html ; q = 0.7");

        Assert.Equal("text/html", qv.Value);
        Assert.Equal(0.7, qv.Quality);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_support_three_decimal_places()
    {
        var qv = QualityValue.Parse("gzip;q=0.999");

        Assert.Equal("gzip", qv.Value);
        Assert.Equal(0.999, qv.Quality);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_clamp_quality_above_one()
    {
        var qv = QualityValue.Parse("text/html;q=1.5");

        Assert.Equal(1.0, qv.Quality);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_mark_zero_quality_as_not_acceptable()
    {
        var qv = QualityValue.Parse("text/html;q=0");

        Assert.True(qv.IsNotAcceptable);
        Assert.Equal(0.0, qv.Quality);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_compare_preferring_higher_quality()
    {
        var qv1 = QualityValue.Parse("text/html;q=0.8");
        var qv2 = QualityValue.Parse("text/plain;q=0.5");

        Assert.True(qv1.CompareTo(qv2) < 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.4.2")]
    public void QualityValue_should_parse_list_and_sort_by_quality()
    {
        var list = QualityValue.ParseList("text/html;q=0.5, text/plain, text/xml;q=0.9");

        Assert.Equal(3, list.Count);
        Assert.Equal(1.0, list[0].Quality);
        Assert.Equal(0.9, list[1].Quality);
        Assert.Equal(0.5, list[2].Quality);
    }
}
