using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Headers;

public sealed class FieldValidatorSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_accept_valid_field_name()
    {
        FieldValidator.ValidateFieldName("content-type", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_accept_field_name_with_digits()
    {
        FieldValidator.ValidateFieldName("x-custom-123", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_accept_field_name_with_special_chars()
    {
        FieldValidator.ValidateFieldName("x-custom!header", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_reject_empty_field_name()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldName("", "RFC-9110-5.1", "RFC-9110-5.1"));

        Assert.Contains("Empty field name", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_reject_uppercase_in_field_name()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldName("Content-Type", "RFC-9110-5.1", "RFC-9110-5.1"));

        Assert.Contains("uppercase", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_reject_space_in_field_name()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldName("content type", "RFC-9110-5.1", "RFC-9110-5.1"));

        Assert.Contains("invalid character", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_reject_colon_in_field_name()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldName("content:type", "RFC-9110-5.1", "RFC-9110-5.1"));

        Assert.Contains("invalid character", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_accept_valid_field_value()
    {
        FieldValidator.ValidateFieldValue("content-type", "text/plain", "RFC-9110-5.2");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_accept_empty_field_value()
    {
        FieldValidator.ValidateFieldValue("content-length", "", "RFC-9110-5.2");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_accept_field_value_with_special_chars()
    {
        FieldValidator.ValidateFieldValue("content-type", "text/plain;charset=utf-8", "RFC-9110-5.2");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_reject_nul_in_field_value()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldValue("test-header", "value\0test", "RFC-9110-5.2"));

        Assert.Contains("NUL", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_reject_cr_in_field_value()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldValue("test-header", "value\rtest", "RFC-9110-5.2"));

        Assert.Contains("CR", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_reject_lf_in_field_value()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldValue("test-header", "value\ntest", "RFC-9110-5.2"));

        Assert.Contains("LF", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_accept_hyphenated_field_names()
    {
        FieldValidator.ValidateFieldName("x-custom-header", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_accept_single_char_field_name()
    {
        FieldValidator.ValidateFieldName("x", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_accept_whitespace_in_field_value()
    {
        FieldValidator.ValidateFieldValue("content-type", "text/plain; charset=utf-8", "RFC-9110-5.2");
    }

    [Theory(Timeout = 5000)]
    [InlineData("!")]
    [InlineData("#")]
    [InlineData("$")]
    [InlineData("%")]
    [InlineData("&")]
    [InlineData("'")]
    [InlineData("*")]
    [InlineData("+")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("^")]
    [InlineData("_")]
    [InlineData("`")]
    [InlineData("|")]
    [InlineData("~")]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_accept_token_characters(string tokenChar)
    {
        FieldValidator.ValidateFieldName($"x{tokenChar}custom", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Theory(Timeout = 5000)]
    [InlineData("\t")]
    [InlineData("@")]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("?")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("\"")]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData("=")]
    [InlineData(" ")]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_reject_invalid_token_characters(string invalidChar)
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldName($"x{invalidChar}custom", "RFC-9110-5.1", "RFC-9110-5.1"));

        Assert.Contains("invalid character", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_accept_lowercase_field_names()
    {
        FieldValidator.ValidateFieldName("content-type", "RFC-9110-5.1", "RFC-9110-5.1");
        FieldValidator.ValidateFieldName("x-custom", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_reject_mixed_case_field_names()
    {
        var ex = Assert.Throws<HttpProtocolException>(
            () => FieldValidator.ValidateFieldName("X-Custom", "RFC-9110-5.1", "RFC-9110-5.1"));

        Assert.Contains("uppercase", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.1")]
    public void FieldValidator_should_handle_numeric_field_names()
    {
        FieldValidator.ValidateFieldName("x-123", "RFC-9110-5.1", "RFC-9110-5.1");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.2")]
    public void FieldValidator_should_handle_long_field_values()
    {
        var longValue = new string('a', 1000);
        FieldValidator.ValidateFieldValue("test-header", longValue, "RFC-9110-5.2");
    }
}
