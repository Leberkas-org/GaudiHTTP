namespace TurboHTTP.Protocol.Syntax.Http3;

internal static class FieldValidator
{
    private const string UppercaseSection = "RFC 9114 §4.2";
    private const string TokenSection = "RFC 9114 §10.3";
    private const string FieldValueSection = "RFC 9114 §10.3";
    private const string ConnectionSection = "RFC 9114 §4.2";

    public static void Validate(IReadOnlyList<(string Name, string Value)> headers) =>
        Semantics.FieldValidator.Validate(
            headers,
            static h => h.Name,
            static h => h.Value,
            UppercaseSection,
            TokenSection,
            FieldValueSection,
            ConnectionSection);

    internal static void ValidateFieldName(string name) =>
        Semantics.FieldValidator.ValidateFieldName(name, UppercaseSection, TokenSection);

    internal static void ValidateConnectionSpecific(string name, string value) =>
        Semantics.FieldValidator.ValidateConnectionSpecific(name, value, ConnectionSection);

    public static void ValidateResponsePseudoHeaders(IReadOnlyList<(string Name, string Value)> headers) =>
        Semantics.PseudoHeaderValidator.ValidateResponsePseudoHeaders(
            headers,
            static h => h.Name,
            "RFC 9114 §4.3.2");
}
