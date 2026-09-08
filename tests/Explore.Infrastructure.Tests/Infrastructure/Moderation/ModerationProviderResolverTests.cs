using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Settings.Groups;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Moderation;
using Explore.Infrastructure.Services.Moderation.Coop;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class ModerationProviderResolverTests
{
    [Test]
    public async Task SyncReportAsync_WhenDisabled_ReturnsDisabledWithoutProviderCall()
    {
        var resolver = CreateResolver(new ModerationProviderOptions
        {
            Enabled = false
        });

        var result = await resolver.SyncReportAsync(CreateProviderEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ProviderDisabled).IsTrue();
        await Assert.That(result.Error!.Category).IsEqualTo("provider_disabled");
    }

    [Test]
    public async Task SyncReportAsync_WhenLocalOnly_IgnoresCompositeSwitchesAndStaysLocal()
    {
        var resolver = CreateResolver(new ModerationProviderOptions
        {
            Mode = ModerationProviderOptions.ModeLocalOnly,
            EvaluateSignals = true,
            MirrorReviewQueue = true
        });

        var result = await resolver.SyncReportAsync(CreateProviderEnvelope());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.ProviderDisabled).IsFalse();
        await Assert.That(result.Signals).IsEmpty();
        await Assert.That(result.ProviderCaseId).IsNull();
    }

    [Test]
    public async Task ExecuteAsync_WhenDecisionExecutionDisabled_ReturnsNonRetryableFailure()
    {
        var resolver = CreateResolver(new ModerationProviderOptions
        {
            ExecuteDecisions = false
        });

        var result = await resolver.ExecuteAsync(CreateDecisionEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IsRetryable).IsFalse();
        await Assert.That(result.Error!.Category).IsEqualTo("decision_execution_disabled");
    }

    [Test]
    public async Task MirrorCaseAsync_WhenCoopEnabled_UsesCoopProvider()
    {
        var resolver = CreateResolver(
            new ModerationProviderOptions
            {
                Mode = ModerationProviderOptions.ModeCoop,
                MirrorReviewQueue = true
            },
            new CoopProviderOptions
            {
                Enabled = true,
                EndpointUrl = "https://coop.example",
                MirrorPath = "/api/v1/items"
            },
            new StaticJsonHandler("""
            {
              "provider_case_id": "coop-case-1",
              "provider_url": "https://coop.example/cases/coop-case-1"
            }
            """));

        var result = await resolver.MirrorCaseAsync(CreateReviewCaseEnvelope());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.ProviderCaseId).IsEqualTo("coop-case-1");
        await Assert.That(result.ProviderUrl).IsEqualTo("https://coop.example/cases/coop-case-1");
    }

    [Test]
    public async Task ConfigureInfrastructureServices_RegistersRuntimeProviderForModerationContracts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reporting:Mode"] = ModerationProviderOptions.ModeLocalOnly
            })
            .Build();

        services.ConfigureInfrastructureServices(configuration);
        services.AddScoped<IReportingRoutingPolicyResolver>(_ => new StaticReportingRoutingPolicyResolver(CreatePolicy()));

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var reportProvider = scope.ServiceProvider.GetRequiredService<IEventReportProvider>();
        var signalProvider = scope.ServiceProvider.GetRequiredService<IModerationSignalProvider>();
        var reviewQueueProvider = scope.ServiceProvider.GetRequiredService<IReviewQueueProvider>();
        var decisionExecutor = scope.ServiceProvider.GetRequiredService<IReportDecisionExecutor>();

        await Assert.That(reportProvider).IsTypeOf<RuntimeModerationProviderResolver>();
        await Assert.That(signalProvider).IsTypeOf<RuntimeModerationProviderResolver>();
        await Assert.That(reviewQueueProvider).IsTypeOf<RuntimeModerationProviderResolver>();
        await Assert.That(decisionExecutor).IsTypeOf<RuntimeModerationProviderResolver>();
    }

    [Test]
    public async Task CompositeProvider_WhenSignalSwitchEnabled_ReturnsSignalsWithoutReviewQueue()
    {
        var signalProvider = new RecordingSignalProvider
        {
            Result = EventSafetySignalProviderResult.Success([CreateSignalEnvelope()])
        };
        var reviewProvider = new RecordingReviewQueueProvider();
        var provider = new CompositeEventReportProvider(
            new LocalEventReportProvider(),
            signalProvider,
            reviewProvider,
            new StaticReportingRoutingPolicyResolver(CreatePolicy(ospreyTargets:
            [
                new ReportingProviderTarget(EventReportExternalProvider.Osprey, EventReportProviderTargetScope.Instance, "instance")
            ])));

        var result = await provider.SyncReportAsync(CreateProviderEnvelope());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Signals).Count().IsEqualTo(1);
        await Assert.That(result.ProviderSignalId).IsEqualTo("signal-1");
        await Assert.That(signalProvider.Calls).IsEqualTo(1);
        await Assert.That(reviewProvider.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task CompositeProvider_WhenReviewQueueFails_ReturnsRetryableFailure()
    {
        var reviewProvider = new RecordingReviewQueueProvider
        {
            Result = ReviewCaseSyncResult.Failure("coop_timeout", isTransient: true)
        };
        var provider = new CompositeEventReportProvider(
            new LocalEventReportProvider(),
            new RecordingSignalProvider(),
            reviewProvider,
            new StaticReportingRoutingPolicyResolver(CreatePolicy(coopTargets:
            [
                new ReportingProviderTarget(EventReportExternalProvider.Coop, EventReportProviderTargetScope.Instance, "instance")
            ])));

        var result = await provider.SyncReportAsync(CreateProviderEnvelope());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IsRetryable).IsTrue();
        await Assert.That(result.Error!.Category).IsEqualTo("coop_timeout");
        await Assert.That(reviewProvider.Calls).IsEqualTo(1);
    }

    private static RuntimeModerationProviderResolver CreateResolver(
        ModerationProviderOptions options,
        CoopProviderOptions? coopOptions = null,
        HttpMessageHandler? coopHandler = null)
    {
        var noopSignal = new NoopModerationSignalProvider();
        var noopReview = new NoopReviewQueueProvider();
        var optionsMonitor = new StaticOptionsMonitor<ModerationProviderOptions>(options);
        var local = new LocalEventReportProvider();
        var composite = new CompositeEventReportProvider(local, noopSignal, noopReview, new StaticReportingRoutingPolicyResolver(CreatePolicy()));
        var osprey = new OspreyModerationSignalProvider(
            new StaticHttpClientFactory(new HttpClient(new StaticOkHandler())),
            new StaticOptionsMonitor<OspreyProviderOptions>(new OspreyProviderOptions()),
            NullLogger<OspreyModerationSignalProvider>.Instance);
        var coop = new CoopReviewQueueProvider(
            new StaticHttpClientFactory(new HttpClient(coopHandler ?? new StaticOkHandler())),
            new StaticOptionsMonitor<CoopProviderOptions>(coopOptions ?? new CoopProviderOptions()),
            NullLogger<CoopReviewQueueProvider>.Instance);

        return new RuntimeModerationProviderResolver(
            local,
            composite,
            osprey,
            coop,
            noopSignal,
            noopReview,
            new StaticReportingRoutingPolicyResolver(CreatePolicy()),
            optionsMonitor,
            Substitute.For<ILogger<RuntimeModerationProviderResolver>>());
    }

    private static EventReportProviderEnvelope CreateProviderEnvelope() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "spam",
        "safety",
        "submitted",
        "open",
        "normal",
        DateTime.UtcNow,
        null,
        "sync-key",
        "correlation-1");

    private static ReportDecisionExecutionEnvelope CreateDecisionEnvelope() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        EventReportDecisionKind.NoViolation,
        "spam",
        "safe note",
        null,
        "decision-key",
        "correlation-1");

    private static ReviewCaseEnvelope CreateReviewCaseEnvelope() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        EventReportExternalProvider.Coop,
        "safety",
        "open",
        "normal",
        "spam",
        DateTime.UtcNow,
        DateTime.UtcNow.AddHours(48),
        "sync-key",
        "correlation-1");

    private static EventSafetySignalEnvelope CreateSignalEnvelope() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        EventReportSignalProvider.Osprey,
        "policy_match",
        "event.spam",
        0.9m,
        EventReportSignalVerdict.LikelyViolation,
        EventReportRecommendedAction.LightModerate,
        "Safe summary",
        "signal-1",
        "correlation-1",
        DateTime.UtcNow);

    private static ReportingRoutingPolicy CreatePolicy(
        IReadOnlyList<ReportingProviderTarget>? ospreyTargets = null,
        IReadOnlyList<ReportingProviderTarget>? coopTargets = null) => new(
        LocalCanonicalRequired: true,
        ExternalSyncEnabled: true,
        InstanceOspreyEnabled: ospreyTargets?.Count > 0,
        TenantOspreyEnabled: false,
        InstanceCoopEnabled: coopTargets?.Count > 0,
        TenantCoopEnabled: false,
        TenantProviderConfigurationLocked: true,
        TenantOspreyProviderLocked: true,
        TenantCoopProviderLocked: true,
        OspreyRoutingMode: ReportingRoutingMode.Both,
        CoopRoutingMode: ReportingRoutingMode.Both,
        EvidenceMode: EventReportProviderEvidenceMode.MetadataOnly,
        OspreyTargets: ospreyTargets ?? [],
        CoopTargets: coopTargets ?? []);

    private sealed class RecordingSignalProvider : IModerationSignalProvider
    {
        public int Calls { get; private set; }
        public EventSafetySignalProviderResult Result { get; init; } = EventSafetySignalProviderResult.Success();

        public Task<EventSafetySignalProviderResult> EvaluateAsync(
            EventReportProviderEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingReviewQueueProvider : IReviewQueueProvider
    {
        public int Calls { get; private set; }
        public ReviewCaseSyncResult Result { get; init; } = ReviewCaseSyncResult.Success("case-1", "https://coop.example/cases/case-1");

        public Task<ReviewCaseSyncResult> MirrorCaseAsync(
            ReviewCaseEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class StaticReportingRoutingPolicyResolver(ReportingRoutingPolicy policy) : IReportingRoutingPolicyResolver
    {
        public Task<ReportingRoutingPolicy> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(policy);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
    }
}
