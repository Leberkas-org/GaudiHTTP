using BenchmarkDotNet.Running;
using GaudiHTTP.Benchmarks.Internal;

// Internal child-process entry used by GaudiClientAllocationBenchmarks' GlobalSetup to run the Kestrel
// server out of process (so client allocation is measured in isolation). Not a user-facing tool.
if (args.Length > 0 && args[0] == ServerProcessHandle.ServerArgument)
{
    await ServerProcessHandle.RunServerAsync();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
