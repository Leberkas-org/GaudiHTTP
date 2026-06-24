using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Encoding;

public sealed class ContentEncodingSupportSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_recognize_gzip_as_supported()
    {
        const string encoding = "gzip";
        var isSupported = ContentEncodingSupport.IsSupported(encoding);

        Assert.True(isSupported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_recognize_deflate_as_supported()
    {
        const string encoding = "deflate";
        var isSupported = ContentEncodingSupport.IsSupported(encoding);

        Assert.True(isSupported);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_recognize_identity_as_supported()
    {
        const string encoding = "identity";
        var isSupported = ContentEncodingSupport.IsSupported(encoding);

        Assert.True(isSupported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_recognize_unknown_encoding_as_unsupported()
    {
        const string encoding = "unknown-codec";
        var isSupported = ContentEncodingSupport.IsSupported(encoding);

        Assert.False(isSupported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_be_case_insensitive()
    {
        var isGzipUpper = ContentEncodingSupport.IsSupported("GZIP");
        var isGzipMixed = ContentEncodingSupport.IsSupported("GZip");

        Assert.True(isGzipUpper);
        Assert.True(isGzipMixed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-12.5.3")]
    public void ContentEncodingSupport_should_handle_x_gzip_equivalent_to_gzip()
    {
        var isXGzipSupported = ContentEncodingSupport.IsSupported("x-gzip");

        Assert.True(isXGzipSupported);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_reject_null_encoding()
    {
        var isSupported = ContentEncodingSupport.IsSupported(null);

        Assert.False(isSupported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_reject_empty_encoding()
    {
        var isSupported = ContentEncodingSupport.IsSupported("");

        Assert.False(isSupported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_reject_whitespace_only_encoding()
    {
        var isSupported = ContentEncodingSupport.IsSupported("   ");

        Assert.False(isSupported);
    }

    [Theory(Timeout = 5000)]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    [InlineData("identity")]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_recognize_standard_encodings(string encoding)
    {
        var isSupported = ContentEncodingSupport.IsSupported(encoding);

        Assert.True(isSupported);
    }

    [Theory(Timeout = 5000)]
    [InlineData("brotli")]
    [InlineData("zstd")]
    [InlineData("unknown")]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_reject_unsupported_encodings(string encoding)
    {
        var isSupported = ContentEncodingSupport.IsSupported(encoding);

        Assert.False(isSupported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_recognize_br_as_supported()
    {
        const string encoding = "br";
        var isSupported = ContentEncodingSupport.IsSupported(encoding);

        Assert.True(isSupported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_return_list_of_supported_codings()
    {
        var supported = ContentEncodingSupport.GetSupportedCodings();

        Assert.NotNull(supported);
        Assert.NotEmpty(supported);
        Assert.Contains("gzip", supported);
        Assert.Contains("deflate", supported);
        Assert.Contains("br", supported);
        Assert.Contains("identity", supported);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.4")]
    public void ContentEncodingSupport_should_return_immutable_list_of_supported_codings()
    {
        var supported1 = ContentEncodingSupport.GetSupportedCodings();
        var supported2 = ContentEncodingSupport.GetSupportedCodings();

        Assert.Same(supported1, supported2);
    }
}
