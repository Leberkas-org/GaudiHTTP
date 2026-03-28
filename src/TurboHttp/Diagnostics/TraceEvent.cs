using System.Diagnostics;

namespace TurboHttp.Diagnostics;

/// <summary>
/// Immutable trace event with deferred message formatting.
/// Stores the template and up to 3 arguments; <see cref="FormatMessage"/>
/// allocates a formatted string only when called.
/// </summary>
public readonly struct TraceEvent
{
    /// <summary>Timestamp from <see cref="Stopwatch.GetTimestamp"/>.</summary>
    public long TimestampTicks { get; }

    /// <summary>Severity level of this event.</summary>
    public TurboTraceLevel Level { get; }

    /// <summary>Category that produced this event.</summary>
    public TurboTraceCategory Category { get; }

    /// <summary>Short type name of the source object (from <c>GetType().Name</c>).</summary>
    public string SourceType { get; }

    /// <summary>Identity hash of the source object (from <c>GetHashCode()</c>).</summary>
    public int SourceHash { get; }

    /// <summary>Format template (compatible with <see cref="string.Format(string,object?)"/>).</summary>
    public string Template { get; }

    private readonly object? _arg0;
    private readonly object? _arg1;
    private readonly object? _arg2;
    private readonly byte _argCount;

    internal TraceEvent(
        long timestampTicks,
        TurboTraceLevel level,
        TurboTraceCategory category,
        string sourceType,
        int sourceHash,
        string template,
        byte argCount,
        object? arg0,
        object? arg1,
        object? arg2)
    {
        TimestampTicks = timestampTicks;
        Level = level;
        Category = category;
        SourceType = sourceType;
        SourceHash = sourceHash;
        Template = template;
        _argCount = argCount;
        _arg0 = arg0;
        _arg1 = arg1;
        _arg2 = arg2;
    }

    /// <summary>
    /// Formats the message by applying stored arguments to the template.
    /// This is the only method that allocates a string.
    /// </summary>
    public string FormatMessage()
    {
        return _argCount switch
        {
            0 => Template,
            1 => string.Format(Template, _arg0),
            2 => string.Format(Template, _arg0, _arg1),
            3 => string.Format(Template, _arg0, _arg1, _arg2),
            _ => Template,
        };
    }
}
