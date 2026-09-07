using Explore.Application.Contracts.Webhooks;
using Explore.Application.Webhooks;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Svix;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class SvixEventTypeSyncServiceTests
{
    [Test]
    public async Task SyncAsync_WhenSvixSyncEnabled_UpsertsCanonicalPublicEventTypes()
    {
        var fixture = new Fixture();
        List<SvixEventTypeSyncRequest> requests = [];
        fixture.SvixClient.UpsertEventTypeAsync(
                Arg.Do<SvixEventTypeSyncRequest>(request => requests.Add(request)),
                Arg.Any<CancellationToken>())
            .Returns(call => new SvixEventTypeSyncResult(call.Arg<SvixEventTypeSyncRequest>().Name));

        var result = await fixture.Service.SyncAsync(CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.SyncedCount).IsEqualTo(13);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(requests.Count).IsEqualTo(13);
        await Assert.That(requests.Select(request => request.Name)).Contains("event.published");
        await Assert.That(requests.Select(request => request.Name)).Contains("webhook.test");
        var published = requests.Single(request => request.Name == "event.published");
        await Assert.That(published.Description).Contains("publicly published");
        await Assert.That(published.GroupName).IsEqualTo("event");
        await Assert.That(published.SchemaJson).Contains("\"title\":\"event.published\"");
        await Assert.That(published.IdempotencyKey).IsEqualTo("svix-event-type:event.published:v1");
    }

    [Test]
    public async Task SyncAsync_WhenProviderIsLocal_ReturnsNoOpWithoutCallingSvix()
    {
        var fixture = new Fixture(new WebhookOptions { Provider = WebhookOptions.ProviderLocal });

        var result = await fixture.Service.SyncAsync(CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.SyncedCount).IsEqualTo(0);
        await Assert.That(result.Failures).IsEmpty();
        await fixture.SvixClient.DidNotReceiveWithAnyArgs()
            .UpsertEventTypeAsync(default!, default);
    }

    [Test]
    public async Task SyncAsync_WhenSvixRateLimitsOneEvent_ReturnsRetryableBoundedFailure()
    {
        var fixture = new Fixture();
        var callCount = 0;
        fixture.SvixClient.UpsertEventTypeAsync(
                Arg.Any<SvixEventTypeSyncRequest>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<SvixEventTypeSyncResult>>(call =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new ApiException(429, "rate limited");
                }

                return Task.FromResult(new SvixEventTypeSyncResult(call.Arg<SvixEventTypeSyncRequest>().Name));
            });

        var result = await fixture.Service.SyncAsync(CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.SyncedCount).IsEqualTo(12);
        await Assert.That(result.Failures.Count).IsEqualTo(1);
        var failure = result.Failures.Single();
        await Assert.That(failure.EventType).IsEqualTo("event.created");
        await Assert.That(failure.FailureCategory).IsEqualTo("svix_provider_unavailable");
        await Assert.That(failure.IsRetryable).IsTrue();
        await Assert.That(failure.SafeDetail).IsEqualTo("SvixApi:429");
    }

    private sealed class Fixture
    {
        public Fixture(WebhookOptions? options = null)
        {
            SvixClient = Substitute.For<ISvixWebhookClient>();
            Service = new SvixEventTypeSyncService(
                SvixClient,
                new WebhookEventTypeRegistry(),
                new WebhookEventSchemaProvider(),
                new StaticOptionsMonitor<WebhookOptions>(options ?? new WebhookOptions { Provider = WebhookOptions.ProviderSvix }));
        }

        public ISvixWebhookClient SvixClient { get; }

        public SvixEventTypeSyncService Service { get; }
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
