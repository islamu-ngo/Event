using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookProviderDispatchIdentityTests
{
    [Test]
    public async Task DispatchAsync_WhenProviderAccepts_QueuesPublicationUsingOnlyFrozenIdentity()
    {
        var fixture = new Fixture();
        SvixProviderPublicationCreateRequest? captured = null;
        fixture.Client.CreatePublicationMessageAsync(
                Arg.Do<SvixProviderPublicationCreateRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(new SvixMessageCreateResult("msg_provider_1"));

        var result = await fixture.Dispatcher.DispatchAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderPublicationDispatchOutcome.ProviderQueued);
        await Assert.That(fixture.Publication.Status).IsEqualTo(WebhookProviderPublicationStatus.ProviderQueued);
        await Assert.That(fixture.Publication.ExternalProviderMessageId).IsEqualTo("msg_provider_1");
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.EventId).IsEqualTo(fixture.Publication.ProviderEventId);
        await Assert.That(captured.IdempotencyKey).IsEqualTo(fixture.Publication.IdempotencyKey);
        await Assert.That(captured.RequestHash).IsEqualTo(fixture.Publication.RequestHash);
        await Assert.That(captured.ProviderApplicationId).IsEqualTo(fixture.Publication.ProviderApplicationId);
        await Assert.That(captured.PayloadBytes).IsEquivalentTo(fixture.Message.GetPayloadBytes()!);
        await Assert.That(captured.PayloadRetentionDays).IsEqualTo(14);
        await Assert.That(fixture.Publication.PublicationRetentionUntil)
            .IsGreaterThan(fixture.Publication.PayloadRetentionUntil);
        await Assert.That(fixture.Message.GetPayloadBytes()).IsNotNull();
    }

    [Test]
    public async Task DispatchAsync_WhenLocalPayloadHorizonExceedsSvixMaximum_ClampsProviderOnly()
    {
        var fixture = new Fixture(payloadRetentionDays: 120);
        SvixProviderPublicationCreateRequest? captured = null;
        fixture.Client.CreatePublicationMessageAsync(
                Arg.Do<SvixProviderPublicationCreateRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(new SvixMessageCreateResult("msg_provider_retention"));

        var result = await fixture.Dispatcher.DispatchAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderPublicationDispatchOutcome.ProviderQueued);
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.PayloadRetentionDays).IsEqualTo(90);
        await Assert.That(fixture.Publication.PayloadRetentionUntil)
            .IsEqualTo(fixture.Publication.PreparedAt.AddDays(120));
        await Assert.That(fixture.Message.PayloadRetentionUntil)
            .IsEqualTo(fixture.Message.MaterializedAt.AddDays(120));
        await Assert.That(fixture.Message.GetPayloadBytes()).IsNotNull();
    }

    [Test]
    public async Task DispatchAsync_WhenDefinitelyNotAccepted_RetriesWithUnchangedIdentity()
    {
        var fixture = new Fixture();
        var captured = new List<SvixProviderPublicationCreateRequest>();
        fixture.Client.CreatePublicationMessageAsync(
                Arg.Do<SvixProviderPublicationCreateRequest>(captured.Add),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => throw SvixWebhookSubmissionException.DefinitelyNotAccepted(
                    "svix_provider_unavailable",
                    isRetryable: true,
                    "SvixApi:429"),
                _ => new SvixMessageCreateResult("msg_provider_2"));

        var first = await fixture.Dispatcher.DispatchAsync(fixture.Claim, CancellationToken.None);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        var retryClaim = fixture.ClaimForRetry();
        var second = await fixture.Dispatcher.DispatchAsync(retryClaim, CancellationToken.None);

        await Assert.That(first.Outcome).IsEqualTo(WebhookProviderPublicationDispatchOutcome.RetryScheduled);
        await Assert.That(second.Outcome).IsEqualTo(WebhookProviderPublicationDispatchOutcome.ProviderQueued);
        await Assert.That(fixture.Publication.AutomaticPublicationAttemptCount).IsEqualTo(2);
        await Assert.That(captured).Count().IsEqualTo(2);
        await Assert.That(captured[1].EventId).IsEqualTo(captured[0].EventId);
        await Assert.That(captured[1].IdempotencyKey).IsEqualTo(captured[0].IdempotencyKey);
        await Assert.That(captured[1].RequestHash).IsEqualTo(captured[0].RequestHash);
        await Assert.That(captured[1].CredentialReference).IsEqualTo(captured[0].CredentialReference);
        await Assert.That(captured[1].CredentialVersion).IsEqualTo(captured[0].CredentialVersion);
        await Assert.That(captured[1].PayloadBytes).IsEquivalentTo(captured[0].PayloadBytes);
    }

    [Test]
    public async Task DispatchAsync_WhenAcceptanceIsAmbiguous_MarksUnknownWithoutBlindRetry()
    {
        var fixture = new Fixture();
        fixture.Client.CreatePublicationMessageAsync(
                Arg.Any<SvixProviderPublicationCreateRequest>(),
                Arg.Any<CancellationToken>())
            .Returns<SvixMessageCreateResult>(_ =>
                throw SvixWebhookSubmissionException.AcceptanceUnknown(
                    "svix_submission_timeout",
                    "TimeoutException"));

        var result = await fixture.Dispatcher.DispatchAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderPublicationDispatchOutcome.PublicationUnknown);
        await Assert.That(fixture.Publication.Status).IsEqualTo(WebhookProviderPublicationStatus.PublicationUnknown);
        await Assert.That(fixture.Publication.ExternalProviderMessageId).IsNull();
        await fixture.Client.Received(1).CreatePublicationMessageAsync(
            Arg.Any<SvixProviderPublicationCreateRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_WhenLeaseExpiresAfterAcceptance_DoesNotPersistStaleCompletionOrResubmit()
    {
        var fixture = new Fixture(leaseDuration: TimeSpan.FromSeconds(30));
        fixture.Client.CreatePublicationMessageAsync(
                Arg.Any<SvixProviderPublicationCreateRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fixture.Time.Advance(TimeSpan.FromMinutes(1));
                return new SvixMessageCreateResult("msg_late");
            });

        var result = await fixture.Dispatcher.DispatchAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderPublicationDispatchOutcome.LeaseLost);
        await Assert.That(fixture.Publication.Status).IsEqualTo(WebhookProviderPublicationStatus.Publishing);
        await Assert.That(fixture.Publication.ExternalProviderMessageId).IsNull();
        await fixture.PublicationRepository.DidNotReceive().UpdateAsync(
            fixture.Publication,
            Arg.Any<CancellationToken>());
        await fixture.Client.Received(1).CreatePublicationMessageAsync(
            Arg.Any<SvixProviderPublicationCreateRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_WhenExactPayloadNoLongerMatchesSnapshot_DeadLettersBeforeProviderCall()
    {
        var fixture = new Fixture(messageHashMatches: false);

        var result = await fixture.Dispatcher.DispatchAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderPublicationDispatchOutcome.DeadLettered);
        await Assert.That(fixture.Publication.Status).IsEqualTo(WebhookProviderPublicationStatus.DeadLettered);
        await Assert.That(fixture.Publication.FailureCategory).IsEqualTo("webhook_publication_snapshot_invalid");
        await fixture.Client.DidNotReceiveWithAnyArgs().CreatePublicationMessageAsync(default!, default);
    }

    private sealed class Fixture
    {
        private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        private static readonly Guid ConsumerId = Guid.Parse("01900000-0000-7000-8000-000000000003");
        private static readonly Guid BindingId = Guid.Parse("01900000-0000-7000-8000-000000000004");
        private static readonly Guid PlanId = Guid.Parse("01900000-0000-7000-8000-000000000005");
        private static readonly DateTime PreparedAt = new(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);
        private static readonly byte[] ExactPayload = "{\"type\":\"event.published\",\"id\":\"evt-1\"}"u8.ToArray();
        private readonly TimeSpan _leaseDuration;

        public Fixture(
            TimeSpan? leaseDuration = null,
            bool messageHashMatches = true,
            int payloadRetentionDays = 14)
        {
            _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(5);
            Time = new MutableTimeProvider(new DateTimeOffset(PreparedAt.AddMinutes(1)));
            MessageRepository = Substitute.For<IWebhookMessageRepository>();
            PublicationRepository = Substitute.For<IWebhookProviderPublicationRepository>();
            Client = Substitute.For<ISvixWebhookClient>();
            Message = CreateMessage(payloadRetentionDays);
            Publication = CreatePublication(
                Message.Id,
                messageHashMatches ? Message.PayloadHash : new string('a', 64).Insert(0, "sha256:"),
                payloadRetentionDays);
            Claim = ClaimForPublishing();

            MessageRepository.GetByTenantAndIdAsync(TenantId, Message.Id, Arg.Any<CancellationToken>())
                .Returns(Message);
            PublicationRepository.UpdateAsync(
                    Arg.Any<WebhookProviderPublication>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<WebhookProviderPublication>());
            Client.CreatePublicationMessageAsync(
                    Arg.Any<SvixProviderPublicationCreateRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(new SvixMessageCreateResult("msg_default"));

            Dispatcher = new WebhookProviderPublicationDispatcher(
                MessageRepository,
                PublicationRepository,
                Client,
                Options.Create(new WebhookProviderPublicationProcessorSettings
                {
                    MaxAutomaticPublicationAttempts = 4,
                    InitialRetryDelaySeconds = 30,
                    MaxRetryDelaySeconds = 300,
                    UnknownReconciliationDelaySeconds = 30
                }),
                Time);
        }

        public IWebhookMessageRepository MessageRepository { get; }

        public IWebhookProviderPublicationRepository PublicationRepository { get; }

        public ISvixWebhookClient Client { get; }

        public MutableTimeProvider Time { get; }

        public WebhookMessage Message { get; }

        public WebhookProviderPublication Publication { get; }

        public WebhookProviderPublicationClaim Claim { get; }

        public WebhookProviderPublicationDispatcher Dispatcher { get; }

        public WebhookProviderPublicationClaim ClaimForRetry()
        {
            var retryAt = Publication.NextActionAt!.Value.AddSeconds(1);
            Time.SetUtcNow(new DateTimeOffset(retryAt));
            return ClaimForPublishing();
        }

        private WebhookProviderPublicationClaim ClaimForPublishing()
        {
            var claimedAt = Time.GetUtcNow().UtcDateTime;
            var dueAt = Publication.NextActionAt ?? Publication.PreparedAt;
            var leaseToken = Guid.CreateVersion7();
            var leaseExpiresAt = claimedAt.Add(_leaseDuration);
            Publication.ClaimForPublishing(
                "provider-worker:test",
                leaseToken,
                leaseExpiresAt,
                claimedAt,
                4);
            return new WebhookProviderPublicationClaim(
                Publication,
                leaseToken,
                Publication.PublicationFence,
                claimedAt,
                leaseExpiresAt,
                dueAt);
        }

        private static WebhookMessage CreateMessage(int payloadRetentionDays) =>
            WebhookMessage.Create(
                TenantId,
                WebhookEventNames.EventPublished,
                "evt-1",
                "Event",
                Guid.Parse("01900000-0000-7000-8000-000000000006"),
                ConsumerId,
                ExactPayload,
                "application/json",
                "utf-8",
                PreparedAt.AddMinutes(-1),
                PreparedAt.AddDays(payloadRetentionDays),
                PreparedAt);

        private static WebhookProviderPublication CreatePublication(
            Guid messageId,
            string requestHash,
            int payloadRetentionDays) =>
            WebhookProviderPublication.Create(
                TenantId,
                messageId,
                PlanId,
                WebhookProviderKind.Svix,
                BindingId,
                "1.96.1",
                "event:stable:evt-1",
                "publication:stable:evt-1",
                requestHash,
                "tenant-consumer-uid",
                "app_provider_1",
                "production",
                "Webhooks:Svix:AuthToken",
                "credential-v3",
                WebhookProviderMode.Svix,
                "configuration-v7",
                1,
                "retention-v2",
                PreparedAt.AddDays(payloadRetentionDays),
                PreparedAt.AddDays(Math.Max(payloadRetentionDays, 30)),
                PreparedAt.AddHours(12),
                PreparedAt);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
