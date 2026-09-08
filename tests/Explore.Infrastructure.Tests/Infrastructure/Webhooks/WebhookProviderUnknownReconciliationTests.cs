using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookProviderUnknownReconciliationTests
{
    [Test]
    public async Task ReconcileAsync_WhenLookupFindsOneExactMatch_QueuesWithoutCreate()
    {
        var fixture = new Fixture();
        fixture.Client.LookupPublicationMessageAsync(
                Arg.Any<SvixProviderPublicationLookupRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(SvixProviderPublicationLookupResult.ExactMatch("msg_exact"));

        var result = await fixture.Reconciler.ReconcileAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderReconciliationOutcome.ProviderQueued);
        await Assert.That(fixture.Publication.Status).IsEqualTo(WebhookProviderPublicationStatus.ProviderQueued);
        await Assert.That(fixture.Publication.ExternalProviderMessageId).IsEqualTo("msg_exact");
        await fixture.Client.DidNotReceiveWithAnyArgs().CreatePublicationMessageAsync(default!, default);
    }

    [Test]
    public async Task ReconcileAsync_WhenLookupProvesAbsence_SchedulesSameIdentityForRetry()
    {
        var fixture = new Fixture();
        SvixProviderPublicationLookupRequest? captured = null;
        fixture.Client.LookupPublicationMessageAsync(
                Arg.Do<SvixProviderPublicationLookupRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(SvixProviderPublicationLookupResult.NotFound());

        var result = await fixture.Reconciler.ReconcileAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderReconciliationOutcome.RetryScheduled);
        await Assert.That(fixture.Publication.Status).IsEqualTo(WebhookProviderPublicationStatus.RetryDue);
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.EventId).IsEqualTo(fixture.Publication.ProviderEventId);
        await Assert.That(captured.RequestHash).IsEqualTo(fixture.Publication.RequestHash);
        await Assert.That(captured.ProviderApplicationId).IsEqualTo(fixture.Publication.ProviderApplicationId);
        await Assert.That(fixture.Publication.Attempts.Last().Outcome)
            .IsEqualTo(WebhookProviderPublicationAttemptOutcome.ProviderAbsenceConfirmed);
        await fixture.Client.DidNotReceiveWithAnyArgs().CreatePublicationMessageAsync(default!, default);
    }

    [Test]
    [Arguments(SvixProviderPublicationLookupOutcome.ConflictingMatch)]
    [Arguments(SvixProviderPublicationLookupOutcome.Ambiguous)]
    public async Task ReconcileAsync_WhenLookupCannotProveExactIdentity_RequiresManual(
        SvixProviderPublicationLookupOutcome lookupOutcome)
    {
        var fixture = new Fixture();
        fixture.Client.LookupPublicationMessageAsync(
                Arg.Any<SvixProviderPublicationLookupRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new SvixProviderPublicationLookupResult(lookupOutcome, null, "provider_lookup_conflict"));

        var result = await fixture.Reconciler.ReconcileAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderReconciliationOutcome.ManualReconciliation);
        await Assert.That(fixture.Publication.Status)
            .IsEqualTo(WebhookProviderPublicationStatus.ManualReconciliation);
        await fixture.Client.DidNotReceiveWithAnyArgs().CreatePublicationMessageAsync(default!, default);
    }

    [Test]
    public async Task ReconcileAsync_WhenCapabilityIsUnproven_RequiresManualWithoutProviderLookup()
    {
        var fixture = new Fixture(lookupSupported: false);

        var result = await fixture.Reconciler.ReconcileAsync(fixture.Claim, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderReconciliationOutcome.ManualReconciliation);
        await Assert.That(fixture.Publication.FailureCategory).IsEqualTo("provider_lookup_unproven");
        await fixture.Client.DidNotReceiveWithAnyArgs().LookupPublicationMessageAsync(default!, default);
        await fixture.Client.DidNotReceiveWithAnyArgs().CreatePublicationMessageAsync(default!, default);
    }

    [Test]
    public async Task ReconcileAsync_WhenIdempotencyExpiresAfterClaim_RequiresManualWithoutLookup()
    {
        var fixture = new Fixture();
        fixture.Publication.RecordAutomaticReconciliationUnresolved(
            fixture.Claim.LeaseToken,
            fixture.Claim.PublicationFence,
            "svix_lookup_unavailable",
            null,
            fixture.Time.GetUtcNow().UtcDateTime.AddMinutes(1),
            fixture.Time.GetUtcNow().UtcDateTime);
        fixture.Time.SetUtcNow(new DateTimeOffset(fixture.Publication.IdempotencyValidUntil.AddSeconds(1)));

        var result = await fixture.Reconciler.ReconcileExpiredOrExhaustedAsync(
            fixture.Publication,
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookProviderReconciliationOutcome.ManualReconciliation);
        await Assert.That(fixture.Publication.FailureCategory).IsEqualTo("provider_idempotency_expired");
        await fixture.Client.DidNotReceiveWithAnyArgs().LookupPublicationMessageAsync(default!, default);
    }

    [Test]
    public async Task ReconcileAsync_WhenLookupRemainsUnavailable_StopsAtConfiguredBound()
    {
        var fixture = new Fixture(maxReconciliationAttempts: 2);
        fixture.Client.LookupPublicationMessageAsync(
                Arg.Any<SvixProviderPublicationLookupRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(SvixProviderPublicationLookupResult.Unavailable("svix_lookup_unavailable"));

        var first = await fixture.Reconciler.ReconcileAsync(fixture.Claim, CancellationToken.None);
        var secondClaim = fixture.ClaimForNextReconciliation();
        var second = await fixture.Reconciler.ReconcileAsync(secondClaim, CancellationToken.None);

        await Assert.That(first.Outcome).IsEqualTo(WebhookProviderReconciliationOutcome.Deferred);
        await Assert.That(second.Outcome).IsEqualTo(WebhookProviderReconciliationOutcome.ManualReconciliation);
        await Assert.That(fixture.Publication.AutomaticReconciliationAttemptCount).IsEqualTo(2);
        await Assert.That(fixture.Publication.LastAutomaticReconciliationAt).IsNotNull();
        await fixture.Client.Received(2).LookupPublicationMessageAsync(
            Arg.Any<SvixProviderPublicationLookupRequest>(),
            Arg.Any<CancellationToken>());
        await fixture.Client.DidNotReceiveWithAnyArgs().CreatePublicationMessageAsync(default!, default);
    }

    private sealed class Fixture
    {
        private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000101");
        private static readonly Guid BindingId = Guid.Parse("01900000-0000-7000-8000-000000000103");
        private static readonly Guid PlanId = Guid.Parse("01900000-0000-7000-8000-000000000104");
        private static readonly DateTime PreparedAt = new(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);
        private readonly int _maxReconciliationAttempts;

        public Fixture(bool lookupSupported = true, int maxReconciliationAttempts = 3)
        {
            _maxReconciliationAttempts = maxReconciliationAttempts;
            Time = new MutableTimeProvider(new DateTimeOffset(PreparedAt.AddMinutes(3)));
            MessageRepository = Substitute.For<IWebhookMessageRepository>();
            PublicationRepository = Substitute.For<IWebhookProviderPublicationRepository>();
            Client = Substitute.For<ISvixWebhookClient>();
            CapabilityPolicy = Substitute.For<IWebhookProviderReconciliationCapabilityPolicy>();
            CapabilityPolicy.SupportsExactMessageLookup(
                    Arg.Any<WebhookProviderKind>(),
                    Arg.Any<string>(),
                    Arg.Any<string>())
                .Returns(lookupSupported);
            Message = CreateMessage();
            Publication = CreateUnknownPublication(Message.Id, Message.PayloadHash);
            Claim = ClaimForReconciliation();
            MessageRepository.GetByTenantAndIdAsync(TenantId, Message.Id, Arg.Any<CancellationToken>())
                .Returns(Message);
            PublicationRepository.UpdateAsync(
                    Arg.Any<WebhookProviderPublication>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<WebhookProviderPublication>());

            Reconciler = new WebhookProviderPublicationReconciler(
                MessageRepository,
                PublicationRepository,
                Client,
                CapabilityPolicy,
                Options.Create(new WebhookProviderPublicationProcessorSettings
                {
                    MaxAutomaticReconciliationAttempts = maxReconciliationAttempts,
                    ReconciliationRetryDelaySeconds = 30,
                    InitialRetryDelaySeconds = 30,
                    MaxRetryDelaySeconds = 300
                }),
                Time);
        }

        public IWebhookMessageRepository MessageRepository { get; }

        public IWebhookProviderPublicationRepository PublicationRepository { get; }

        public ISvixWebhookClient Client { get; }

        public IWebhookProviderReconciliationCapabilityPolicy CapabilityPolicy { get; }

        public MutableTimeProvider Time { get; }

        public WebhookMessage Message { get; }

        public WebhookProviderPublication Publication { get; }

        public WebhookProviderPublicationClaim Claim { get; }

        public WebhookProviderPublicationReconciler Reconciler { get; }

        public WebhookProviderPublicationClaim ClaimForNextReconciliation()
        {
            Time.SetUtcNow(new DateTimeOffset(Publication.NextActionAt!.Value.AddSeconds(1)));
            return ClaimForReconciliation();
        }

        private WebhookProviderPublicationClaim ClaimForReconciliation()
        {
            var claimedAt = Time.GetUtcNow().UtcDateTime;
            var dueAt = Publication.NextActionAt ?? Publication.PreparedAt;
            var leaseToken = Guid.CreateVersion7();
            var leaseExpiresAt = claimedAt.AddMinutes(2);
            Publication.ClaimForAutomaticReconciliation(
                "provider-reconciler:test",
                leaseToken,
                leaseExpiresAt,
                claimedAt,
                _maxReconciliationAttempts);
            return new WebhookProviderPublicationClaim(
                Publication,
                leaseToken,
                Publication.PublicationFence,
                claimedAt,
                leaseExpiresAt,
                dueAt);
        }

        private static WebhookMessage CreateMessage() =>
            WebhookMessage.Create(
                TenantId,
                "event.published",
                "evt-unknown",
                "Event",
                Guid.Parse("01900000-0000-7000-8000-000000000105"),
                Guid.Parse("01900000-0000-7000-8000-000000000106"),
                "{\"type\":\"event.published\",\"id\":\"evt-unknown\"}"u8,
                "application/json",
                "utf-8",
                PreparedAt.AddMinutes(-1),
                PreparedAt.AddDays(14),
                PreparedAt);

        private static WebhookProviderPublication CreateUnknownPublication(
            Guid messageId,
            string requestHash)
        {
            var publication = WebhookProviderPublication.Create(
                TenantId,
                messageId,
                PlanId,
                WebhookProviderKind.Svix,
                BindingId,
                "1.96.1",
                "event:stable:evt-unknown",
                "publication:stable:evt-unknown",
                requestHash,
                "tenant-consumer-uid",
                "app_provider_unknown",
                "production",
                "Webhooks:Svix:AuthToken",
                "credential-v3",
                WebhookProviderMode.Svix,
                "configuration-v7",
                1,
                "retention-v2",
                PreparedAt.AddDays(14),
                PreparedAt.AddDays(30),
                PreparedAt.AddHours(12),
                PreparedAt);
            var publishingToken = Guid.CreateVersion7();
            publication.ClaimForPublishing(
                "provider-worker:test",
                publishingToken,
                PreparedAt.AddMinutes(2),
                PreparedAt.AddMinutes(1),
                4);
            publication.MarkPublicationUnknown(
                publishingToken,
                publication.PublicationFence,
                "svix_submission_timeout",
                "TimeoutException",
                PreparedAt.AddMinutes(3),
                PreparedAt.AddMinutes(1).AddSeconds(30));
            return publication;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
