using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookProviderPublicationDiagnosticTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DispatchContainmentExportsFixedCategoryAndPreservesCallerCancellation(bool canceled)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var time = new PublicationClock(now);
        var publications = Substitute.For<IWebhookProviderPublicationRepository>();
        var messages = Substitute.For<IWebhookMessageRepository>();
        var client = Substitute.For<ISvixWebhookClient>();
        var options = Options.Create(new WebhookProviderPublicationProcessorSettings());
        var publication = WebhookProviderPublication.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            WebhookProviderKind.Svix, Guid.CreateVersion7(), "1.96.1",
            "event:diagnostic", "publication:diagnostic", "sha256:" + new string('a', 64),
            "consumer-diagnostic", "app-diagnostic", "production",
            "Webhooks:Svix:AuthToken", "credential-v1", WebhookProviderMode.Svix,
            "configuration-v1", 1, "retention-v1",
            now.AddDays(1), now.AddDays(30), now.AddHours(12), now);
        Guid lease = Guid.CreateVersion7();
        publication.ClaimForPublishing("provider-worker:test", lease, now.AddMinutes(1), now, 4);
        var claim = new WebhookProviderPublicationClaim(
            publication, lease, publication.PublicationFence, now, now.AddMinutes(1), now);
        publications.ClaimDueAsync(Arg.Any<WebhookProviderPublicationClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebhookProviderPublicationClaim>>([claim]));
        string canary = Guid.CreateVersion7().ToString("N");
        using var cancellation = new CancellationTokenSource();
        messages.GetByTenantAndIdAsync(publication.TenantId, publication.WebhookMessageId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (canceled)
                {
                    cancellation.Cancel();
                    return Task.FromException<WebhookMessage?>(new OperationCanceledException(cancellation.Token));
                }
                return Task.FromException<WebhookMessage?>(new PrivatePublicationDetails(canary));
            });
        var dispatcher = new WebhookProviderPublicationDispatcher(messages, publications, client, options, time);
        var reconciler = new WebhookProviderPublicationReconciler(messages, publications, client,
            Substitute.For<IWebhookProviderReconciliationCapabilityPolicy>(), options, time);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<IMeterFactory>());
        var logs = new PublicationCollector();
        var drain = new WebhookProviderPublicationDrainService(publications, dispatcher, reconciler, options, time, metrics, logs);

        if (canceled)
            await Assert.ThrowsAsync<OperationCanceledException>(() => drain.ProcessBatchAsync(cancellation.Token));
        else
        {
            var result = await drain.ProcessBatchAsync(cancellation.Token);
            await Assert.That(result.FailedCount).IsEqualTo(1);
            await Assert.That(result.ProviderQueuedCount).IsEqualTo(0);
            var exported = logs.Entries.Single();
            await Assert.That(exported.Exception).IsNull();
            await Assert.That(exported.State.Single(field => field.Key == "FailureCategory").Value)
                .IsEqualTo("publication_dispatch_failed");
            await Assert.That(exported.Message).DoesNotContain(canary);
            await Assert.That(exported.Message).DoesNotContain(nameof(PrivatePublicationDetails));
            await Assert.That(exported.State.Any(field => field.Key == "FailureType")).IsFalse();
        }
        if (canceled)
            await Assert.That(logs.Entries.Count).IsEqualTo(0);
    }

    private sealed class PublicationClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class PrivatePublicationDetails(string canary) : Exception(canary);

    private sealed class PublicationCollector : ILogger<WebhookProviderPublicationDrainService>
    {
        public List<(string Message, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception,
                state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToList() : []));
    }
}
