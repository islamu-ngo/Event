using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class WebhookProviderPublicationStateTests
{
    private static readonly DateTime PreparedAt =
        new(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PublishRetryDeadLetterAndManualResolution_AppendsOrderedFencedEvidence()
    {
        var publication = CreatePublication();
        var firstLeaseToken = Guid.CreateVersion7();

        publication.ClaimForPublishing(
            "publisher-1",
            firstLeaseToken,
            PreparedAt.AddMinutes(3),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 3);
        publication.ScheduleRetry(
            firstLeaseToken,
            publication.PublicationFence,
            "provider_unavailable",
            "temporary outage",
            PreparedAt.AddMinutes(4),
            PreparedAt.AddMinutes(2));

        var secondLeaseToken = Guid.CreateVersion7();
        publication.ClaimForPublishing(
            "publisher-2",
            secondLeaseToken,
            PreparedAt.AddMinutes(7),
            PreparedAt.AddMinutes(5),
            maxAutomaticPublicationAttempts: 3);
        publication.DeadLetter(
            secondLeaseToken,
            publication.PublicationFence,
            "retry_budget_exhausted",
            null,
            PreparedAt.AddMinutes(6));
        publication.RequireManualReconciliation(
            "operator_review_required",
            null,
            PreparedAt.AddMinutes(7));
        publication.ResolveManuallyAsProviderQueued(
            "provider-message-123",
            PreparedAt.AddMinutes(8));

        var attempts = publication.Attempts.ToArray();
        await Assert.That(publication.Status).IsEqualTo(WebhookProviderPublicationStatus.ProviderQueued);
        await Assert.That(publication.ExternalProviderMessageId).IsEqualTo("provider-message-123");
        await Assert.That(publication.AutomaticPublicationAttemptCount).IsEqualTo(2);
        await Assert.That(publication.PublicationFence).IsEqualTo(2);
        await Assert.That(attempts.Length).IsEqualTo(6);
        await Assert.That(attempts[0].Outcome).IsEqualTo(WebhookProviderPublicationAttemptOutcome.PublishingStarted);
        await Assert.That(attempts[1].Outcome).IsEqualTo(WebhookProviderPublicationAttemptOutcome.RetryScheduled);
        await Assert.That(attempts[2].Outcome).IsEqualTo(WebhookProviderPublicationAttemptOutcome.PublishingStarted);
        await Assert.That(attempts[3].Outcome).IsEqualTo(WebhookProviderPublicationAttemptOutcome.DeadLettered);
        await Assert.That(attempts[4].Outcome).IsEqualTo(WebhookProviderPublicationAttemptOutcome.ManualReconciliationRequired);
        await Assert.That(attempts[5].Outcome).IsEqualTo(WebhookProviderPublicationAttemptOutcome.ReconciledProviderQueued);
        await Assert.That(attempts.Select(attempt => attempt.AttemptNumber).ToArray())
            .IsEquivalentTo([1, 2, 3, 4, 5, 6]);
        await Assert.That(attempts[0].PublicationFence).IsEqualTo(1);
        await Assert.That(attempts[2].PublicationFence).IsEqualTo(2);
        await Assert.That(attempts[5].PublicationFence).IsEqualTo(2);
    }

    [Test]
    public async Task TimeoutAfterPotentialAcceptance_BecomesUnknownAndReconcilesWithoutFreshIdentity()
    {
        var publication = CreatePublication();
        var providerEventId = publication.ProviderEventId;
        var idempotencyKey = publication.IdempotencyKey;
        var requestHash = publication.RequestHash;
        var publishLeaseToken = Guid.CreateVersion7();

        publication.ClaimForPublishing(
            "publisher",
            publishLeaseToken,
            PreparedAt.AddMinutes(3),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 2);
        publication.MarkPublicationUnknown(
            publishLeaseToken,
            publication.PublicationFence,
            "acceptance_timeout",
            "provider response was not observed",
            PreparedAt.AddMinutes(4),
            PreparedAt.AddMinutes(2));

        await Assert.That(publication.Status).IsEqualTo(WebhookProviderPublicationStatus.PublicationUnknown);
        await Assert.That(publication.ExternalProviderMessageId).IsNull();

        var reconciliationLeaseToken = Guid.CreateVersion7();
        publication.ClaimForAutomaticReconciliation(
            "reconciler",
            reconciliationLeaseToken,
            PreparedAt.AddMinutes(7),
            PreparedAt.AddMinutes(5),
            maxAutomaticReconciliationAttempts: 2);
        publication.MarkProviderQueued(
            reconciliationLeaseToken,
            publication.PublicationFence,
            "provider-message-456",
            PreparedAt.AddMinutes(6));

        await Assert.That(publication.Status).IsEqualTo(WebhookProviderPublicationStatus.ProviderQueued);
        await Assert.That(publication.ProviderEventId).IsEqualTo(providerEventId);
        await Assert.That(publication.IdempotencyKey).IsEqualTo(idempotencyKey);
        await Assert.That(publication.RequestHash).IsEqualTo(requestHash);
        await Assert.That(publication.Attempts.Last().Outcome)
            .IsEqualTo(WebhookProviderPublicationAttemptOutcome.ReconciledProviderQueued);
    }

    [Test]
    public async Task Completion_WithStaleLeaseTokenFenceOrExpiredLease_IsRejectedWithoutEvidence()
    {
        var publication = CreatePublication();
        var leaseToken = Guid.CreateVersion7();

        publication.ClaimForPublishing(
            "publisher",
            leaseToken,
            PreparedAt.AddMinutes(3),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 2);
        var attemptCount = publication.Attempts.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.MarkProviderQueued(
                Guid.CreateVersion7(),
                publication.PublicationFence,
                "provider-message",
                PreparedAt.AddMinutes(2));
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.MarkProviderQueued(
                leaseToken,
                publication.PublicationFence + 1,
                "provider-message",
                PreparedAt.AddMinutes(2));
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.MarkProviderQueued(
                leaseToken,
                publication.PublicationFence,
                "provider-message",
                PreparedAt.AddMinutes(3));
            return Task.CompletedTask;
        });

        await Assert.That(publication.Status).IsEqualTo(WebhookProviderPublicationStatus.Publishing);
        await Assert.That(publication.ExternalProviderMessageId).IsNull();
        await Assert.That(publication.Attempts.Count).IsEqualTo(attemptCount);
    }

    [Test]
    public async Task AutomaticClaims_AtOrAfterIdempotencyExpiry_AreRejected()
    {
        var publication = CreatePublication(idempotencyValidity: TimeSpan.FromMinutes(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.ClaimForPublishing(
                "publisher",
                Guid.CreateVersion7(),
                PreparedAt.AddMinutes(12),
                PreparedAt.AddMinutes(10),
                maxAutomaticPublicationAttempts: 2);
            return Task.CompletedTask;
        });

        var leaseToken = Guid.CreateVersion7();
        publication.ClaimForPublishing(
            "publisher",
            leaseToken,
            PreparedAt.AddMinutes(4),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 2);
        publication.MarkPublicationUnknown(
            leaseToken,
            publication.PublicationFence,
            "acceptance_timeout",
            null,
            PreparedAt.AddMinutes(5),
            PreparedAt.AddMinutes(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.ClaimForAutomaticReconciliation(
                "reconciler",
                Guid.CreateVersion7(),
                PreparedAt.AddMinutes(12),
                PreparedAt.AddMinutes(10),
                maxAutomaticReconciliationAttempts: 2);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Creation_WithIdempotencyWindowOverTwelveHours_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        {
            _ = CreatePublication(idempotencyValidity: TimeSpan.FromHours(12).Add(TimeSpan.FromTicks(1)));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task AutomaticAttemptLimits_AreEnforcedAcrossRetriesAndReconciliation()
    {
        var publication = CreatePublication();
        var firstLeaseToken = Guid.CreateVersion7();

        publication.ClaimForPublishing(
            "publisher",
            firstLeaseToken,
            PreparedAt.AddMinutes(3),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 1);
        publication.ScheduleRetry(
            firstLeaseToken,
            publication.PublicationFence,
            "provider_unavailable",
            null,
            PreparedAt.AddMinutes(4),
            PreparedAt.AddMinutes(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.ClaimForPublishing(
                "publisher",
                Guid.CreateVersion7(),
                PreparedAt.AddMinutes(7),
                PreparedAt.AddMinutes(5),
                maxAutomaticPublicationAttempts: 1);
            return Task.CompletedTask;
        });

        var unknownPublication = CreatePublication();
        var publishLeaseToken = Guid.CreateVersion7();
        unknownPublication.ClaimForPublishing(
            "publisher",
            publishLeaseToken,
            PreparedAt.AddMinutes(3),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 1);
        unknownPublication.MarkPublicationUnknown(
            publishLeaseToken,
            unknownPublication.PublicationFence,
            "acceptance_timeout",
            null,
            PreparedAt.AddMinutes(4),
            PreparedAt.AddMinutes(2));
        var reconciliationLeaseToken = Guid.CreateVersion7();
        unknownPublication.ClaimForAutomaticReconciliation(
            "reconciler",
            reconciliationLeaseToken,
            PreparedAt.AddMinutes(7),
            PreparedAt.AddMinutes(5),
            maxAutomaticReconciliationAttempts: 1);
        unknownPublication.RecordAutomaticReconciliationUnresolved(
            reconciliationLeaseToken,
            unknownPublication.PublicationFence,
            "not_found_yet",
            null,
            PreparedAt.AddMinutes(8),
            PreparedAt.AddMinutes(6));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            unknownPublication.ClaimForAutomaticReconciliation(
                "reconciler",
                Guid.CreateVersion7(),
                PreparedAt.AddMinutes(11),
                PreparedAt.AddMinutes(9),
                maxAutomaticReconciliationAttempts: 1);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task InvalidTransitions_FromPreparedOrQueued_AreRejected()
    {
        var publication = CreatePublication();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.RequireManualReconciliation(
                "operator_review_required",
                null,
                PreparedAt.AddMinutes(1));
            return Task.CompletedTask;
        });

        var leaseToken = Guid.CreateVersion7();
        publication.ClaimForPublishing(
            "publisher",
            leaseToken,
            PreparedAt.AddMinutes(3),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 1);
        publication.MarkProviderQueued(
            leaseToken,
            publication.PublicationFence,
            "provider-message",
            PreparedAt.AddMinutes(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.ClaimForPublishing(
                "publisher",
                Guid.CreateVersion7(),
                PreparedAt.AddMinutes(5),
                PreparedAt.AddMinutes(4),
                maxAutomaticPublicationAttempts: 2);
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            publication.Abandon("operator_abandoned", null, PreparedAt.AddMinutes(4));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Abandon_FromDeadLettered_SettlesWithAppendOnlyEvidence()
    {
        var publication = CreatePublication();
        var leaseToken = Guid.CreateVersion7();
        publication.ClaimForPublishing(
            "publisher",
            leaseToken,
            PreparedAt.AddMinutes(3),
            PreparedAt.AddMinutes(1),
            maxAutomaticPublicationAttempts: 1);
        publication.DeadLetter(
            leaseToken,
            publication.PublicationFence,
            "retry_budget_exhausted",
            null,
            PreparedAt.AddMinutes(2));

        publication.Abandon(
            "operator_abandoned",
            "operator confirmed that no provider message exists",
            PreparedAt.AddMinutes(3));

        await Assert.That(publication.Status).IsEqualTo(WebhookProviderPublicationStatus.Abandoned);
        await Assert.That(publication.Attempts.Last().Outcome)
            .IsEqualTo(WebhookProviderPublicationAttemptOutcome.Abandoned);
        await Assert.That(publication.ConcurrencyVersion).IsEqualTo(4);
    }

    [Test]
    public async Task ImmutablePlanAndProviderIdentity_HaveNoPublicMutationSurfaceOrProviderTargetSnapshot()
    {
        string[] immutableProperties =
        [
            nameof(WebhookProviderPublication.WebhookMessageId),
            nameof(WebhookProviderPublication.WebhookDeliveryPlanSnapshotId),
            nameof(WebhookProviderPublication.ProviderKindId),
            nameof(WebhookProviderPublication.ProviderBindingId),
            nameof(WebhookProviderPublication.ProviderVersion),
            nameof(WebhookProviderPublication.ProviderEventId),
            nameof(WebhookProviderPublication.IdempotencyKey),
            nameof(WebhookProviderPublication.RequestHash),
            nameof(WebhookProviderPublication.ApplicationUid),
            nameof(WebhookProviderPublication.ProviderApplicationId),
            nameof(WebhookProviderPublication.ProviderEnvironment),
            nameof(WebhookProviderPublication.CredentialReference),
            nameof(WebhookProviderPublication.CredentialVersion),
            nameof(WebhookProviderPublication.ModeSnapshotId),
            nameof(WebhookProviderPublication.ProviderConfigurationVersion),
            nameof(WebhookProviderPublication.EventContractVersion),
            nameof(WebhookProviderPublication.RetentionPolicyVersion),
            nameof(WebhookProviderPublication.PayloadRetentionUntil),
            nameof(WebhookProviderPublication.PublicationRetentionUntil),
            nameof(WebhookProviderPublication.IdempotencyValidUntil)
        ];

        foreach (var propertyName in immutableProperties)
        {
            var property = typeof(WebhookProviderPublication).GetProperty(propertyName);
            await Assert.That(property).IsNotNull();
            await Assert.That(property!.SetMethod?.IsPublic ?? false).IsFalse();
        }

        await Assert.That(typeof(WebhookProviderPublication).GetProperties()
                .Any(property => property.Name.Contains("ProviderTargetSnapshot", StringComparison.Ordinal)))
            .IsFalse();
        await Assert.That(typeof(WebhookProviderPublication).Assembly
                .GetType("Explore.Domain.WebhookProviderTargetSnapshot"))
            .IsNull();
    }

    private static WebhookProviderPublication CreatePublication(TimeSpan? idempotencyValidity = null) =>
        WebhookProviderPublication.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            WebhookProviderKind.Svix,
            Guid.CreateVersion7(),
            "svix-2026.07",
            "event-123",
            "idempotency-123",
            $"sha256:{new string('a', 64)}",
            "consumer-application-uid",
            "provider-application-id",
            "managed-eu",
            "secret:webhook-provider",
            "credential-v3",
            WebhookProviderMode.Svix,
            "provider-config-v5",
            eventContractVersion: 4,
            "retention-v2",
            PreparedAt.AddDays(7),
            PreparedAt.AddDays(30),
            PreparedAt.Add(idempotencyValidity ?? TimeSpan.FromHours(12)),
            PreparedAt);
}
