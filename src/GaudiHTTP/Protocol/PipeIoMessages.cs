using System.IO.Pipelines;

namespace GaudiHTTP.Protocol;

internal sealed record ReadCompleted(ReadResult Result, int Gen);
internal sealed record ReadFailed(Exception Ex, int Gen);
internal sealed record FlushCompleted(FlushResult Result, int Gen);
internal sealed record FlushFailed(Exception Ex, int Gen);

internal sealed class PipeReadState(int gen)
{
    public readonly Func<ReadResult, object> Success = result => new ReadCompleted(result, gen);
    public readonly Func<Exception, object> Failure = ex => new ReadFailed(ex, gen);
}

internal sealed class PipeFlushState(int gen)
{
    public readonly Func<FlushResult, object> Success = result => new FlushCompleted(result, gen);
    public readonly Func<Exception, object> Failure = ex => new FlushFailed(ex, gen);
}

