using System.Threading.Tasks.Sources;
using GaudiHTTP.Internal;

namespace GaudiHTTP.Tests.Client;

public sealed class PendingRequestPoolingSpec
{
    [Fact(Timeout = 5000)]
    public void Rent_Returns_PendingRequest()
    {
        var pending = PendingRequest.Rent();

        Assert.NotNull(pending);
        Assert.IsType<PendingRequest>(pending);
    }

    [Fact(Timeout = 5000)]
    public void Rent_ReturnsNewInstance_WhenPoolEmpty()
    {
        var pending1 = PendingRequest.Rent();
        PendingRequest.Return(pending1);

        var pending2 = PendingRequest.Rent();

        Assert.Same(pending1, pending2);
    }

    [Fact(Timeout = 5000)]
    public void Return_PushesInstanceBackToPool()
    {
        var pending = PendingRequest.Rent();
        var version1 = pending.Version;

        PendingRequest.Return(pending);

        var reused = PendingRequest.Rent();
        var version2 = reused.Version;

        Assert.Same(pending, reused);
        // Version increments after Reset() in Rent()
        Assert.NotEqual(version1, version2);
    }

    [Fact(Timeout = 5000)]
    public void Version_ReturnsCurrentVersionToken()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;

        Assert.IsType<short>(version);
    }

    [Fact(Timeout = 5000)]
    public async Task GetValueTask_ReturnsAwaitableTask()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        var task = pending.GetValueTask();

        // Set result asynchronously
        _ = Task.Run(async () =>
        {
            await Task.Yield();
            pending.TrySetResult(response, version);
        }, TestContext.Current.CancellationToken);

        var result = await task;

        Assert.Same(response, result);
    }

    [Fact(Timeout = 5000)]
    public void TrySetResult_WithCorrectVersion_ReturnsTrue()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        var result = pending.TrySetResult(response, version);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void TrySetResult_WithWrongVersion_ReturnsFalse()
    {
        var pending = PendingRequest.Rent();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        var result = pending.TrySetResult(response, (short)(pending.Version + 1));

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public void TrySetResult_WhenAlreadySet_ReturnsFalse()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var response1 = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        var response2 = new HttpResponseMessage(System.Net.HttpStatusCode.Created);

        pending.TrySetResult(response1, version);
        var result = pending.TrySetResult(response2, version);

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public void TrySetException_WithValidException_ReturnsTrue()
    {
        var pending = PendingRequest.Rent();
        var exception = new InvalidOperationException("test");

        var result = pending.TrySetException(exception, pending.Version);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void TrySetException_WhenAlreadySet_ReturnsFalse()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        var exception = new InvalidOperationException("test");

        pending.TrySetResult(response, version);
        var result = pending.TrySetException(exception, version);

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public async Task TrySetException_ResolvesValueTaskWithException()
    {
        var pending = PendingRequest.Rent();
        var task = pending.GetValueTask();
        var exception = new InvalidOperationException("test error");

        pending.TrySetException(exception, pending.Version);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.Equal("test error", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void TrySetCanceled_WithDefaultCancellationToken_ReturnsTrue()
    {
        var pending = PendingRequest.Rent();

        var result = pending.TrySetCanceled(TestContext.Current.CancellationToken, pending.Version);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void TrySetCanceled_WithCancellationToken_ReturnsTrue()
    {
        var pending = PendingRequest.Rent();
        using var cts = new CancellationTokenSource();

        var result = pending.TrySetCanceled(cts.Token, pending.Version);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task TrySetCanceled_ResolvesValueTaskWithOperationCanceledException()
    {
        var pending = PendingRequest.Rent();
        var task = pending.GetValueTask();

        pending.TrySetCanceled(TestContext.Current.CancellationToken, pending.Version);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
    }

    [Fact(Timeout = 5000)]
    public void GetResult_WithCorrectVersion_ReturnsResponse()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        pending.TrySetResult(response, version);
        var result = pending.GetResult(version);

        Assert.Same(response, result);
    }

    [Fact(Timeout = 5000)]
    public void GetStatus_Pending_ReturnsStatusPending()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;

        var status = pending.GetStatus(version);

        Assert.Equal(ValueTaskSourceStatus.Pending, status);
    }

    [Fact(Timeout = 5000)]
    public void GetStatus_AfterSetResult_ReturnsStatusSucceeded()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        pending.TrySetResult(response, version);
        var status = pending.GetStatus(version);

        Assert.Equal(ValueTaskSourceStatus.Succeeded, status);
    }

    [Fact(Timeout = 5000)]
    public void GetStatus_AfterSetException_ReturnsStatusFaulted()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var exception = new InvalidOperationException("test");

        pending.TrySetException(exception, version);
        var status = pending.GetStatus(version);

        Assert.Equal(ValueTaskSourceStatus.Faulted, status);
    }

    [Fact(Timeout = 5000)]
    public async Task OnCompleted_RegistersContinuation()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        pending.OnCompleted(
            _ => { tcs.TrySetResult(); },
            null,
            version,
            ValueTaskSourceOnCompletedFlags.None);

        pending.TrySetResult(response, version);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task OnCompleted_WithUseSchedulingContext_RegistersCorrectly()
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        pending.OnCompleted(
            _ => { tcs.TrySetResult(); },
            null,
            version,
            ValueTaskSourceOnCompletedFlags.UseSchedulingContext);

        pending.TrySetCanceled(TestContext.Current.CancellationToken, version);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void MultipleRents_GenerateUniqueVersions()
    {
        var pending1 = PendingRequest.Rent();
        var version1 = pending1.Version;
        PendingRequest.Return(pending1);

        var pending2 = PendingRequest.Rent();
        var version2 = pending2.Version;

        // Same instance, but version increments
        Assert.Same(pending1, pending2);
        Assert.NotEqual(version1, version2);
    }

    [Fact(Timeout = 5000)]
    public async Task VersionGuard_PreventsStaleCompletion()
    {
        var pending = PendingRequest.Rent();
        var originalVersion = pending.Version;
        var task1 = pending.GetValueTask();

        // Simulate returning to pool and renting again
        PendingRequest.Return(pending);
        var reused = PendingRequest.Rent();
        var newVersion = reused.Version;

        var response1 = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        var response2 = new HttpResponseMessage(System.Net.HttpStatusCode.Created);

        // Try to complete with old version (should fail)
        var oldVersionResult = reused.TrySetResult(response1, originalVersion);

        // Complete with new version (should succeed)
        var newVersionResult = reused.TrySetResult(response2, newVersion);

        Assert.False(oldVersionResult);
        Assert.True(newVersionResult);

        // task1 should never complete because we failed to set result with originalVersion
        var completed = await Task.WhenAny(
            Task.Run(async () => await task1),
            Task.Delay(100, TestContext.Current.CancellationToken));

        Assert.NotSame(completed, Task.Run(async () => await task1));
    }

    // Reproduces the TrailerSpec cross-test flake: a cancellation callback registered against one
    // request's pooled PendingRequest can fire AFTER that instance was returned to the process-global
    // pool and re-rented by a different in-flight request. Without a version guard, TrySetCanceled
    // would cancel the unrelated new renter and its real response would be dropped. The version guard
    // makes the stale cancel a no-op.
    [Fact(Timeout = 5000)]
    public async Task TrySetCanceled_WithStaleVersion_DoesNotCancelRecycledPending()
    {
        // Rent P for request R1; capture the version the cancel callback is bound to.
        var pending = PendingRequest.Rent();
        var staleVersion = pending.Version;

        // R1 completes and P is returned to the global pool.
        pending.TrySetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK), staleVersion);
        _ = await pending.GetValueTask();
        PendingRequest.Return(pending);

        // A different in-flight request R2 re-rents the SAME pooled instance (version bumped).
        var reused = PendingRequest.Rent();
        var newVersion = reused.Version;
        Assert.Same(pending, reused);
        Assert.NotEqual(staleVersion, newVersion);

        var r2Task = reused.GetValueTask();

        // The stale cancel callback from R1 fires late, still bound to the OLD version.
        var staleCancelResult = reused.TrySetCanceled(TestContext.Current.CancellationToken, staleVersion);

        // The version guard must reject it — R2 is untouched.
        Assert.False(staleCancelResult);

        // R2's real response still completes it, and awaiting yields that response (not a cancellation).
        var expected = new HttpResponseMessage(System.Net.HttpStatusCode.Created);
        Assert.True(reused.TrySetResult(expected, newVersion));
        Assert.Same(expected, await r2Task);

        PendingRequest.Return(reused);
    }
}