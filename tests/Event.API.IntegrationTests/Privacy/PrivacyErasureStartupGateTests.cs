using Event.Api.IntegrationTests.Fixtures;
using Explore.API.BackgroundServices;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Privacy;

public sealed class PrivacyErasureStartupGateTests
{
    [Test]
    public async Task CoLocatedTopology_ReplaysBeforeStartup()
    {
        var replay = new RecordingReplay();
        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IOptions<PrivacyErasureDurabilityOptions>>(Options.Create(new PrivacyErasureDurabilityOptions()))
            .AddScoped<IPrivacyErasureReplayService>(_ => replay)
            .BuildServiceProvider();

        await PrivacyErasureStartupGate.RunAsync(services, CancellationToken.None);

        await Assert.That(replay.Called).IsTrue();
    }

    [Test]
    public async Task ReplayFailure_PreventsHostedWorkerInvocation()
    {
        await using var factory = new ReplayFailureFactory();

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.Run(() => factory.CreateClient()));

        await Assert.That(factory.HostedWorkerStarted).IsFalse();
        await Assert.That(exception!.Message).Contains("API startup is blocked");
        await Assert.That(exception.Message).DoesNotContain(ReplayFailure.RawProviderMessage);
    }

    [Test]
    public async Task Cancellation_PropagatesWithoutStartingHostedWorker()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IOptions<PrivacyErasureDurabilityOptions>>(Options.Create(new PrivacyErasureDurabilityOptions
            {
                Topology = PrivacyErasureAuthorityTopology.ExternalDatabase
            }))
            .AddScoped<IPrivacyErasureReplayService, ReplayCancellation>()
            .BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            PrivacyErasureStartupGate.RunAsync(services, cancellation.Token));
    }

    [Test]
    public async Task ReplayInvariantFailure_ReportsOnlyBoundedReasonCode()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddScoped<IPrivacyErasureReplayService, StaleRestoreReplay>()
            .BuildServiceProvider();

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PrivacyErasureStartupGate.RunAsync(services, CancellationToken.None));

        await Assert.That(exception!.Message).Contains("stale_restore_below_retained_floor");
        await Assert.That(exception.Message).DoesNotContain(nameof(StaleRestoreBelowRetainedFloorException));
    }

    [Test]
    public async Task UnknownReplayException_MapsToGenericBoundedReasonCode()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddScoped<IPrivacyErasureReplayService, UntrustedReasonReplay>()
            .BuildServiceProvider();

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PrivacyErasureStartupGate.RunAsync(services, CancellationToken.None));

        await Assert.That(exception!.Message).Contains("privacy_erasure_replay_failed");
        await Assert.That(exception.Message).DoesNotContain(UntrustedReplayException.Canary);
    }

    private sealed class ReplayFailureFactory : AuthenticatedWebApplicationFactory
    {
        private readonly StartupMarker _marker = new();

        public ReplayFailureFactory()
        {
            AdditionalConfiguration["Testing:EnablePrivacyErasureStartupGate"] = "true";
            AdditionalConfiguration["PrivacyErasure:Authority:Topology"] = "ExternalDatabase";
            AdditionalConfiguration["ConnectionStrings:PrivacyErasureAuthority"] =
                "Host=unused;Database=unused;Username=unused";
        }

        public bool HostedWorkerStarted => _marker.Started;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<PrivacyErasureDurabilityOptions>(options =>
                {
                    options.Topology = PrivacyErasureAuthorityTopology.ExternalDatabase;
                });
                services.RemoveAll<IPrivacyErasureReplayService>();
                services.AddScoped<IPrivacyErasureReplayService, ReplayFailure>();
                services.AddSingleton(_marker);
                services.AddHostedService<StartupMarkerHostedService>();
            });
        }
    }

    private sealed class ReplayFailure : IPrivacyErasureReplayService
    {
        public const string RawProviderMessage = "provider-endpoint-and-secret-canary";

        public Task ReplayAsync(CancellationToken cancellationToken) =>
            throw new IOException(RawProviderMessage);
    }

    private sealed class RecordingReplay : IPrivacyErasureReplayService
    {
        public bool Called { get; private set; }

        public Task ReplayAsync(CancellationToken cancellationToken)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ReplayCancellation : IPrivacyErasureReplayService
    {
        public Task ReplayAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class StaleRestoreReplay : IPrivacyErasureReplayService
    {
        public Task ReplayAsync(CancellationToken cancellationToken) =>
            throw new StaleRestoreBelowRetainedFloorException();
    }

    private sealed class UntrustedReasonReplay : IPrivacyErasureReplayService
    {
        public Task ReplayAsync(CancellationToken cancellationToken) =>
            throw new UntrustedReplayException();
    }

    private sealed class UntrustedReplayException()
        : PrivacyErasureReplayException(Canary)
    {
        public const string Canary = "subject-and-secret-canary";
    }

    private sealed class StartupMarker
    {
        public bool Started { get; set; }
    }

    private sealed class StartupMarkerHostedService(StartupMarker marker) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            marker.Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
