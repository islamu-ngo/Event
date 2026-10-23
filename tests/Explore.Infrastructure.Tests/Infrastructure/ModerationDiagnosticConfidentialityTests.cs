using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Moderation;
using Explore.Infrastructure.Services.Moderation.Coop;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class ModerationDiagnosticConfidentialityTests
{
    [Test]
    public async Task GrpcProviderDetailCannotReachFinalLoggerWhileStatusAndRetryCategorySurvive()
    {
        var canary = $"{Guid.NewGuid():N}\r\nprivate-grpc-detail";
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = HttpProtocols.Http2));
        await using var app = builder.Build();
        var requests = 0;
        app.Run(context =>
        {
            Interlocked.Increment(ref requests);
            context.Response.ContentType = "application/grpc";
            context.Response.Headers["grpc-status"] = "14";
            context.Response.Headers["grpc-message"] = Uri.EscapeDataString(canary);
            return Task.CompletedTask;
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StartAsync(deadline.Token);
        try
        {
            using var client = new HttpClient();
            var logger = new TestListLogger<OspreyModerationSignalProvider>();
            var provider = new OspreyModerationSignalProvider(new ClientFactory(client),
                new SelectedOptions<OspreyProviderOptions>(new OspreyProviderOptions
                {
                    Enabled = true,
                    Transport = OspreyProviderOptions.TransportGrpc,
                    GrpcEndpointUrl = app.Urls.Single(),
                    AllowLocalProviderEndpoints = true
                }), logger);

            var result = await provider.EvaluateAsync(ReportEnvelope(canary), deadline.Token);

            await Assert.That(Volatile.Read(ref requests)).IsEqualTo(1);
            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Error!.IsTransient).IsTrue();
            await Assert.That(logger.Entries.Count).IsEqualTo(1);
            await AssertSafe(logger.Entries, [canary[..32]]);
            await Assert.That(logger.Entries.Single().State.Any(property =>
                property.Key == "StatusCode" && property.Value?.ToString() == "Unavailable")).IsTrue();
            await Assert.That(logger.Entries.Single().State.Any(property =>
                property.Key == "FailureCategory" && property.Value?.ToString() == result.Error.Category)).IsTrue();
        }
        finally
        {
            await app.StopAsync(deadline.Token);
        }
    }

    [Test]
    public async Task ProviderTransportFailuresRetainRetryCategoriesWithoutPrivateExceptionsOrEnvelopeData()
    {
        var canary = $"{Guid.NewGuid():N}\r\nprivate-provider";
        using var transport = new FailingTransport(canary);
        using var client = new HttpClient(transport);
        var http = new ClientFactory(client);
        var coopLog = new TestListLogger<CoopReviewQueueProvider>();
        var ospreyLog = new TestListLogger<OspreyModerationSignalProvider>();
        var coop = new CoopReviewQueueProvider(http, new SelectedOptions<CoopProviderOptions>(
            new CoopProviderOptions { Enabled = true, EndpointUrl = "https://coop.example.test" }), coopLog);
        var osprey = new OspreyModerationSignalProvider(http, new SelectedOptions<OspreyProviderOptions>(
            new OspreyProviderOptions { Enabled = true, EndpointUrl = "https://osprey.example.test" }), ospreyLog);
        var report = ReportEnvelope(canary);
        var review = ReviewEnvelope(canary);

        var mirrorResult = await coop.MirrorCaseAsync(review);
        var signalResult = await osprey.EvaluateAsync(report);

        await Assert.That(mirrorResult.Succeeded).IsFalse();
        await Assert.That(mirrorResult.Error!.Category).IsEqualTo("coop_unreachable");
        await Assert.That(mirrorResult.Error.IsTransient).IsTrue();
        await Assert.That(signalResult.Succeeded).IsFalse();
        await Assert.That(signalResult.Error!.Category).IsEqualTo("osprey_unreachable");
        await Assert.That(signalResult.Error.IsTransient).IsTrue();
        await Assert.That(transport.RequestCount).IsEqualTo(2);
        var entries = coopLog.Entries.Concat(ospreyLog.Entries).ToArray();
        await Assert.That(entries.Length).IsEqualTo(2);
        await AssertSafe(entries, [canary[..32], report.ReportId.ToString(), review.CaseId.ToString()]);
        await Assert.That(entries.Select(entry => entry.Message).Distinct().Count()).IsEqualTo(2);
        await Assert.That(JsonSerializer.Serialize(new { mirrorResult, signalResult })
            .Contains(nameof(HttpRequestException), StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RuntimeProviderFailureRetainsSelectedRetryableResultWithoutPrivateExceptionExports()
    {
        var canary = $"{Guid.NewGuid():N}\r\nprivate-options";
        using var client = new HttpClient();
        var http = new ClientFactory(client);
        var coop = new CoopReviewQueueProvider(http,
            new UnavailableOptions<CoopProviderOptions>(canary), new TestListLogger<CoopReviewQueueProvider>());
        var osprey = new OspreyModerationSignalProvider(http,
            new SelectedOptions<OspreyProviderOptions>(new OspreyProviderOptions()),
            new TestListLogger<OspreyModerationSignalProvider>());
        var local = new LocalEventReportProvider();
        var routing = Substitute.For<IReportingRoutingPolicyResolver>();
        var logger = new TestListLogger<RuntimeModerationProviderResolver>();
        var runtime = new RuntimeModerationProviderResolver(local,
            new CompositeEventReportProvider(local, osprey, coop, routing), osprey, coop,
            new NoopModerationSignalProvider(), new NoopReviewQueueProvider(), routing,
            new SelectedOptions<ModerationProviderOptions>(new ModerationProviderOptions
            {
                Mode = ModerationProviderOptions.ModeCoop,
                MirrorReviewQueue = true
            }), logger);

        var result = await runtime.MirrorCaseAsync(ReviewEnvelope(canary));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Category).IsEqualTo("review_queue_provider_failed");
        await Assert.That(result.Error.IsTransient).IsTrue();
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await AssertSafe(logger.Entries, [canary[..32]]);
        await Assert.That(JsonSerializer.Serialize(result).Contains(nameof(IOException), StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task IntakeResolutionFailureRetainsFailClosedDecisionWithoutTenantOrExceptionExports()
    {
        var canary = $"{Guid.NewGuid():N}\r\nprivate-settings";
        var tenantId = Guid.NewGuid();
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        settings.ResolveGroupAsync<ReportingIntakeSettingGroup>(Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReportingIntakeSettingGroup>(new InvalidOperationException(canary)));
        var logger = new TestListLogger<EventReportingIntakeGuard>();
        var guard = new EventReportingIntakeGuard(settings, logger);

        var result = await guard.ResolveAsync(tenantId, CancellationToken.None);

        await Assert.That(result.TenantResolved).IsTrue();
        await Assert.That(result.IntakeEnabled).IsFalse();
        await Assert.That(result.ReasonCode).IsEqualTo("event_reporting_intake_disabled");
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await AssertSafe(logger.Entries, [canary[..32], tenantId.ToString()]);
        await Assert.That(JsonSerializer.Serialize(result).Contains(canary[..32], StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertSafe(IEnumerable<TestLogEntry> entries, string[] canaries)
    {
        foreach (var entry in entries)
        {
            await Assert.That(entry.Exception).IsNull();
            var finalOutput = entry.Message + JsonSerializer.Serialize(entry.State);
            foreach (var canary in canaries)
                await Assert.That(finalOutput.Contains(canary, StringComparison.Ordinal)).IsFalse();
        }
    }

    private static EventReportProviderEnvelope ReportEnvelope(string canary) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "spam", "safety", "submitted", "open", "normal", DateTime.UtcNow, DateTime.UtcNow,
        Guid.NewGuid().ToString("N"), canary[..32], EventReportProviderEvidenceMode.MetadataOnly);

    private static ReviewCaseEnvelope ReviewEnvelope(string canary) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        EventReportExternalProvider.Coop, "safety", "open", "normal", "spam",
        DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid().ToString("N"), canary[..32],
        EventReportProviderEvidenceMode.MetadataOnly);

    private sealed class SelectedOptions<T>(T options) : IOptionsMonitor<T>
    {
        public T CurrentValue => options;
        public T Get(string? name) => options;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class UnavailableOptions<T>(string canary) : IOptionsMonitor<T>
    {
        public T CurrentValue => throw new IOException(canary);
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FailingTransport(string canary) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromException<HttpResponseMessage>(new HttpRequestException(canary, new IOException(canary)));
        }
    }
}
