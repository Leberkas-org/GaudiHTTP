namespace GaudiHTTP.Protocol.Syntax.Http3.Qpack;

internal sealed class EncoderInstruction
{
    public EncoderInstructionType Type { get; init; }

    /// <summary>Set Dynamic Table Capacity value, or Duplicate index.</summary>
    public int IntValue { get; init; }

    /// <summary>Insert With Name Reference: name index.</summary>
    public int NameIndex { get; init; }

    /// <summary>Insert With Name Reference: true if static table.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Insert With Name Reference / Literal Name: header name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Insert instructions: header value.</summary>
    public string Value { get; init; } = string.Empty;
}
