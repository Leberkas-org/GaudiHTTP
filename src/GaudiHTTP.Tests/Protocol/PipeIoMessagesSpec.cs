using System.IO.Pipelines;
using GaudiHTTP.Protocol;

namespace GaudiHTTP.Tests.Protocol;

public sealed class PipeIoMessagesSpec
{
    [Fact(Timeout = 5000)]
    public void PipeReadState_should_produce_correctly_generationed_messages()
    {
        var state = new PipeReadState(42);
        var result = new ReadResult(default, isCanceled: false, isCompleted: false);

        var msg = (ReadCompleted)state.Success(result);

        Assert.Equal(42, msg.Gen);
        Assert.Equal(result, msg.Result);
    }

    [Fact(Timeout = 5000)]
    public void PipeReadState_should_produce_correctly_generationed_failure_messages()
    {
        var state = new PipeReadState(7);
        var ex = new InvalidOperationException("test");

        var msg = (ReadFailed)state.Failure(ex);

        Assert.Equal(7, msg.Gen);
        Assert.Same(ex, msg.Ex);
    }

    [Fact(Timeout = 5000)]
    public void PipeFlushState_should_produce_correctly_generationed_messages()
    {
        var state = new PipeFlushState(99);
        var result = new FlushResult(isCanceled: false, isCompleted: false);

        var msg = (FlushCompleted)state.Success(result);

        Assert.Equal(99, msg.Gen);
        Assert.Equal(result, msg.Result);
    }

    [Fact(Timeout = 5000)]
    public void PipeFlushState_should_produce_correctly_generationed_failure_messages()
    {
        var state = new PipeFlushState(3);
        var ex = new TimeoutException("flush timeout");

        var msg = (FlushFailed)state.Failure(ex);

        Assert.Equal(3, msg.Gen);
        Assert.Same(ex, msg.Ex);
    }

    [Fact(Timeout = 5000)]
    public void PipeReadState_delegates_should_be_stable_across_calls()
    {
        var state = new PipeReadState(1);

        var s1 = state.Success;
        var s2 = state.Success;
        var f1 = state.Failure;
        var f2 = state.Failure;

        Assert.Same(s1, s2);
        Assert.Same(f1, f2);
    }

    [Fact(Timeout = 5000)]
    public void PipeFlushState_delegates_should_be_stable_across_calls()
    {
        var state = new PipeFlushState(1);

        var s1 = state.Success;
        var s2 = state.Success;
        var f1 = state.Failure;
        var f2 = state.Failure;

        Assert.Same(s1, s2);
        Assert.Same(f1, f2);
    }

    [Fact(Timeout = 5000)]
    public void Different_generations_should_produce_different_delegate_instances()
    {
        var state1 = new PipeReadState(1);
        var state2 = new PipeReadState(2);

        Assert.NotSame(state1.Success, state2.Success);
        Assert.NotSame(state1.Failure, state2.Failure);
    }
}
