using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHttp.Client;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Tests the public API surface for <see cref="IClientStreamOwner"/>,
/// <see cref="StreamInitializationOptions"/>, and <see cref="StreamInitializationResult"/>.
/// Verifies that advanced users can create and interact with the actor-based
/// stream lifecycle through the public interface.
/// </summary>
public sealed class ClientStreamOwnerApiTests : TestKit
{
    private static readonly TurboClientOptions DefaultOptions = new();

    private static TurboRequestOptions DefaultRequestOptions() =>
        new(null, new HttpRequestMessage().Headers, HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionOrLower, TimeSpan.FromSeconds(30), 0);

    // ── IClientStreamOwner is accessible from public API ──────────────────

    [Fact(DisplayName = "RFC-9110-api-001: IClientStreamOwner exposes ActorRef", Timeout = 5000)]
    public void IClientStreamOwner_ExposesActorRef()
    {
        IClientStreamOwner owner = ClientStreamOwnerWrapper.Create(Sys);

        Assert.NotNull(owner.ActorRef);
        Assert.False(owner.ActorRef.IsNobody());
    }

    // ── StreamInitializationOptions construction ──────────────────────────

    [Fact(DisplayName = "RFC-9110-api-002: StreamInitializationOptions defaults SupervisorStrategy to null", Timeout = 5000)]
    public void StreamInitializationOptions_DefaultsSupervisorStrategy_ToNull()
    {
        var options = new StreamInitializationOptions(DefaultOptions, DefaultRequestOptions);

        Assert.Equal(DefaultOptions, options.ClientOptions);
        Assert.NotNull(options.RequestOptionsFactory);
        Assert.Null(options.SupervisorStrategy);
    }

    [Fact(DisplayName = "RFC-9110-api-003: StreamInitializationOptions accepts custom SupervisorStrategy", Timeout = 5000)]
    public void StreamInitializationOptions_AcceptsCustomSupervisorStrategy()
    {
        var strategy = new AllForOneStrategy(5, TimeSpan.FromMinutes(5), ex => Directive.Restart);
        var options = new StreamInitializationOptions(DefaultOptions, DefaultRequestOptions, strategy);

        Assert.Same(strategy, options.SupervisorStrategy);
    }

    // ── StreamInitializationResult pattern matching ───────────────────────

    [Fact(DisplayName = "RFC-9110-api-004: StreamInitializationResult.Success carries InstanceRef", Timeout = 5000)]
    public void StreamInitializationResult_Success_CarriesInstanceRef()
    {
        var probe = CreateTestProbe();
        StreamInitializationResult result = new StreamInitializationResult.Success(probe.Ref);

        Assert.IsType<StreamInitializationResult.Success>(result);

        var success = (StreamInitializationResult.Success)result;
        Assert.Equal(probe.Ref, success.InstanceRef);
    }

    [Fact(DisplayName = "RFC-9110-api-005: StreamInitializationResult.Failed carries Exception", Timeout = 5000)]
    public void StreamInitializationResult_Failed_CarriesException()
    {
        var ex = new InvalidOperationException("test failure");
        StreamInitializationResult result = new StreamInitializationResult.Failed(ex);

        Assert.IsType<StreamInitializationResult.Failed>(result);

        var failed = (StreamInitializationResult.Failed)result;
        Assert.Same(ex, failed.Reason);
    }

    [Fact(DisplayName = "RFC-9110-api-006: StreamInitializationResult supports pattern matching", Timeout = 5000)]
    public void StreamInitializationResult_SupportsPatternMatching()
    {
        var probe = CreateTestProbe();
        StreamInitializationResult result = new StreamInitializationResult.Success(probe.Ref);

        var matched = result switch
        {
            StreamInitializationResult.Success s => $"ok:{s.InstanceRef.Path.Name}",
            StreamInitializationResult.Failed f => $"fail:{f.Reason.Message}",
            _ => "unknown"
        };

        Assert.StartsWith("ok:", matched);
    }

    // ── InitializeStreamAsync ────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9110-api-007: InitializeStreamAsync returns Success on valid options", Timeout = 10000)]
    public async Task InitializeStreamAsync_ReturnsSuccess_OnValidOptions()
    {
        IClientStreamOwner owner = ClientStreamOwnerWrapper.Create(Sys);
        var options = new StreamInitializationOptions(DefaultOptions, DefaultRequestOptions);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await owner.InitializeStreamAsync(options, cts.Token);

        Assert.IsType<StreamInitializationResult.Success>(result);
        var success = (StreamInitializationResult.Success)result;
        Assert.NotNull(success.InstanceRef);
        Assert.False(success.InstanceRef.IsNobody());
    }

    [Fact(DisplayName = "RFC-9110-api-008: InitializeStreamAsync respects CancellationToken", Timeout = 5000)]
    public async Task InitializeStreamAsync_RespectsCancellationToken()
    {
        IClientStreamOwner owner = ClientStreamOwnerWrapper.Create(Sys);
        var options = new StreamInitializationOptions(DefaultOptions, DefaultRequestOptions);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => owner.InitializeStreamAsync(options, cts.Token));
    }

    // ── WithSupervisorStrategy builder extension ─────────────────────────

    [Fact(DisplayName = "RFC-9110-api-009: TurboClientDescriptor stores custom SupervisorStrategy", Timeout = 5000)]
    public void TurboClientDescriptor_StoresCustomSupervisorStrategy()
    {
        var strategy = new OneForOneStrategy(10, TimeSpan.FromMinutes(1), ex => Directive.Resume);
        var descriptor = new TurboClientDescriptor
        {
            CustomSupervisorStrategy = strategy
        };

        Assert.Same(strategy, descriptor.CustomSupervisorStrategy);
    }
}
