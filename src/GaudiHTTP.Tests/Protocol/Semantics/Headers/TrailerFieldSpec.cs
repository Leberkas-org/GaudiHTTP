using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.Headers;

public sealed class TrailerFieldSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_parse_single_trailer_field_name()
    {
        const string trailerHeader = "Content-MD5";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Single(fieldNames);
        Assert.Contains("content-md5", fieldNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_parse_multiple_trailer_field_names()
    {
        const string trailerHeader = "Content-MD5, X-Checksum, X-Signature";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Equal(3, fieldNames.Count);
        Assert.Contains("content-md5", fieldNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("x-checksum", fieldNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("x-signature", fieldNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_be_case_insensitive()
    {
        const string trailerHeader = "CONTENT-MD5, content-type, Content-Length";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Equal(3, fieldNames.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_handle_whitespace_around_field_names()
    {
        const string trailerHeader = "  Content-MD5  ,  X-Checksum  ";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Equal(2, fieldNames.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_handle_empty_trailer_header()
    {
        const string trailerHeader = "";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Empty(fieldNames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_handle_null_trailer_header()
    {
        var fieldNames = TrailerFieldValidator.Parse(null);

        Assert.NotNull(fieldNames);
        Assert.Empty(fieldNames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_handle_whitespace_only_header()
    {
        const string trailerHeader = "   ";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Empty(fieldNames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_reject_hop_by_hop_headers()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer("transfer-encoding");

        Assert.False(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_reject_connection_header()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer("connection");

        Assert.False(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_reject_content_length_in_trailer()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer("content-length");

        Assert.False(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_reject_trailer_header_in_trailer()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer("trailer");

        Assert.False(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_allow_custom_trailer_fields()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer("x-custom-signature");

        Assert.True(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_allow_content_md5_in_trailer()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer("content-md5");

        Assert.True(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_be_case_insensitive_for_validation()
    {
        var isAllowedLower = TrailerFieldValidator.IsAllowedInTrailer("content-md5");
        var isAllowedUpper = TrailerFieldValidator.IsAllowedInTrailer("CONTENT-MD5");
        var isAllowedMixed = TrailerFieldValidator.IsAllowedInTrailer("Content-MD5");

        Assert.True(isAllowedLower);
        Assert.True(isAllowedUpper);
        Assert.True(isAllowedMixed);
    }

    [Theory(Timeout = 5000)]
    [InlineData("transfer-encoding")]
    [InlineData("content-encoding")]
    [InlineData("connection")]
    [InlineData("keep-alive")]
    [InlineData("proxy-authenticate")]
    [InlineData("proxy-authorization")]
    [InlineData("te")]
    [InlineData("trailer")]
    [InlineData("upgrade")]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_reject_hop_by_hop_and_restricted_headers(string fieldName)
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer(fieldName);

        Assert.False(isAllowed);
    }

    [Theory(Timeout = 5000)]
    [InlineData("x-custom")]
    [InlineData("x-signature")]
    [InlineData("x-checksum")]
    [InlineData("content-md5")]
    [InlineData("date")]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void TrailerField_should_allow_permitted_trailer_fields(string fieldName)
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer(fieldName);

        Assert.True(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_handle_trailing_comma()
    {
        const string trailerHeader = "Content-MD5, X-Checksum,";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Equal(2, fieldNames.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_handle_leading_comma()
    {
        const string trailerHeader = ", Content-MD5, X-Checksum";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Equal(2, fieldNames.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_handle_consecutive_commas()
    {
        const string trailerHeader = "Content-MD5,, X-Checksum";
        var fieldNames = TrailerFieldValidator.Parse(trailerHeader);

        Assert.NotNull(fieldNames);
        Assert.Equal(2, fieldNames.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_reject_null_field_name_in_validation()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer(null);

        Assert.False(isAllowed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.2")]
    public void TrailerField_should_reject_empty_field_name_in_validation()
    {
        var isAllowed = TrailerFieldValidator.IsAllowedInTrailer("");

        Assert.False(isAllowed);
    }
}
