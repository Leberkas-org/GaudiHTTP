namespace TurboHTTP.Benchmarks.LoadTest;

internal sealed record LoadTestOptions
{
    public int DurationSeconds { get; init; } = 10;
    public int WarmupSeconds { get; init; } = 3;
    public int Connections { get; init; } = 64;
    public int PipelineDepth { get; init; } = 16;
    public string Route { get; init; } = "/plaintext";
    public string Protocol { get; init; } = "h1";
    public bool RunTurbo { get; init; } = true;
    public bool RunKestrel { get; init; } = true;
    public bool Profile { get; init; }
    public string? Serve { get; init; }
    public int ServerPort { get; init; }

    public static LoadTestOptions Parse(string[] args)
    {
        var options = new LoadTestOptions();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile")
            {
                options = options with { Profile = true };
                continue;
            }

            if (i >= args.Length - 1)
            {
                break;
            }

            switch (args[i])
            {
                case "--duration":
                    options = options with { DurationSeconds = int.Parse(args[++i]) };
                    break;
                case "--warmup":
                    options = options with { WarmupSeconds = int.Parse(args[++i]) };
                    break;
                case "--connections":
                    options = options with { Connections = int.Parse(args[++i]) };
                    break;
                case "--pipeline":
                    options = options with { PipelineDepth = int.Parse(args[++i]) };
                    break;
                case "--route":
                    options = options with { Route = args[++i] };
                    break;
                case "--protocol":
                    options = options with { Protocol = args[++i].ToLowerInvariant() };
                    break;
                case "--server":
                    var s = args[++i].ToLowerInvariant();
                    options = options with
                    {
                        RunTurbo = s is "turbo" or "both",
                        RunKestrel = s is "kestrel" or "both",
                    };
                    break;
                case "--serve":
                    options = options with { Serve = args[++i].ToLowerInvariant() };
                    break;
                case "--server-port":
                    options = options with { ServerPort = int.Parse(args[++i]) };
                    break;
            }
        }

        return options;
    }
}

internal readonly record struct LoadResult(
    string Name,
    long Requests,
    double DurationSeconds,
    double RequestsPerSecond,
    double P50Micros,
    double P99Micros,
    double AllocBytesPerRequest,
    int Gen0,
    int Gen1,
    int Gen2);
