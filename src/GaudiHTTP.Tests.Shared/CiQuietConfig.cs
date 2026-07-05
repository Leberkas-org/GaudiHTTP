using Akka.Configuration;

namespace GaudiHTTP.Tests.Shared;

// CI runs (GitHub Actions sets CI=true) drown test output in Akka INFO/WARNING noise —
// CoordinatedShutdown, dead letters, absorbed stage failures — from the hundreds of short-lived
// test actor systems. This config silences that only in CI; local runs keep full logging.
public static class CiQuietConfig
{
    public static bool IsCi => Environment.GetEnvironmentVariable("CI") is not null;

    public static readonly Config Instance =
        Environment.GetEnvironmentVariable("CI") is null
            ? Config.Empty
            : ConfigurationFactory.ParseString("""
                akka.loglevel = ERROR
                akka.stdout-loglevel = ERROR
                akka.log-dead-letters = off
                akka.log-dead-letters-during-shutdown = off
                """);
}
