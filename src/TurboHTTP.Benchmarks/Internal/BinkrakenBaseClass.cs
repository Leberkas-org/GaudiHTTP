namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Base class for all Binkraken remote HTTPS benchmarks. Provides static URIs
/// for the light (~3 KB HTML) and heavy (~159 KB JS bundle) endpoints.
/// </summary>
public abstract class BinkrakenBaseClass : BenchmarkSuiteBase
{
    /// <summary>
    /// Light endpoint: the SPA index page (~3 KB HTML).
    /// </summary>
    public static readonly Uri LightUri = new("https://binkraken.com/");

    /// <summary>
    /// Heavy endpoint: the largest JS bundle (~159 KB).
    /// </summary>
    public static readonly Uri HeavyUri = new("https://binkraken.com/assets/_plugin-vue_export-helper-Cgmbqv7k.js");
}
