using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookProviderPublicationReconciler(
    IWebhookMessageRepository messageRepository,
    IWebhookProviderPublicationRepository publicationRepository,
    ISvixWebhookClient svixClient,
    IWebhookProviderReconciliationCapabilityPolicy capabilityPolicy,
    IOptions<WebhookProviderPublicationProcessorSettings> settings,
    TimeProvider timeProvider)
{
    private readonly WebhookProviderPublicationProcessorSettings _settings = settings.Value;

    public async Task<WebhookProviderReconciliationResult> ReconcileAsync(
        WebhookProviderPublicationClaim claim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        var publication = claim.Publication;
        var observedAt = GetUtcNow();
        if (!ClaimIsActive(claim, observedAt))
        {
            return WebhookProviderReconciliationResult.LeaseLost();
        }

        if (observedAt >= publication.IdempotencyValidUntil)
        {
            return await RequireManualAsync(
                publication,
                "provider_idempotency_expired",
                null,
                observedAt,
                cancellationToken);
        }

        if (!capabilityPolicy.SupportsExactMessageLookup(
                publication.ProviderKind,
                publication.ProviderVersion,
                publication.ProviderEnvironment))
        {
            return await RequireManualAsync(
                publication,
                "provider_lookup_unproven",
                null,
                observedAt,
                cancellationToken);
        }

        SvixProviderPublicationLookupResult lookup;
        try
        {
            var request = await CreateLookupRequestAsync(publication, cancellationToken);
            if (request is null)
            {
                return await RequireManualAsync(
                    publication,
                    "webhook_publication_snapshot_invalid",
                    null,
                    GetUtcNow(),
                    cancellationToken);
            }

            lookup = await svixClient.LookupPublicationMessageAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            lookup = SvixProviderPublicationLookupResult.Unavailable("svix_lookup_unavailable");
        }

        return lookup.Outcome switch
        {
            SvixProviderPublicationLookupOutcome.ExactMatch =>
                await MarkProviderQueuedAsync(claim, lookup, cancellationToken),
            SvixProviderPublicationLookupOutcome.NotFound =>
                await ScheduleRetryAsync(claim, cancellationToken),
            SvixProviderPublicationLookupOutcome.Unavailable =>
                await DeferOrRequireManualAsync(claim, lookup, cancellationToken),
            SvixProviderPublicationLookupOutcome.Unsupported =>
                await RequireManualAsync(
                    publication,
                    lookup.FailureCategory ?? "provider_lookup_unsupported",
                    null,
                    GetUtcNow(),
                    cancellationToken),
            SvixProviderPublicationLookupOutcome.ConflictingMatch =>
                await RequireManualAsync(
                    publication,
                    "provider_lookup_conflict",
                    lookup.FailureCategory,
                    GetUtcNow(),
                    cancellationToken),
            _ => await RequireManualAsync(
                publication,
                "provider_lookup_ambiguous",
                lookup.FailureCategory,
                GetUtcNow(),
                cancellationToken)
        };
    }

    public async Task<WebhookProviderReconciliationResult> ReconcileExpiredOrExhaustedAsync(
        WebhookProviderPublication publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publication);
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = GetUtcNow();
        if (publication.Status != WebhookProviderPublicationStatus.PublicationUnknown ||
            publication.ProcessingLeaseExpiresAt > observedAt ||
            (publication.IdempotencyValidUntil > observedAt &&
             publication.AutomaticReconciliationAttemptCount <
                 _settings.MaxAutomaticReconciliationAttempts))
        {
            return WebhookProviderReconciliationResult.LeaseLost();
        }

        var failureCategory = publication.IdempotencyValidUntil <= observedAt
            ? "provider_idempotency_expired"
            : "provider_reconciliation_exhausted";
        return await RequireManualAsync(
            publication,
            failureCategory,
            null,
            observedAt,
            cancellationToken);
    }

    private async Task<WebhookProviderReconciliationResult> MarkProviderQueuedAsync(
        WebhookProviderPublicationClaim claim,
        SvixProviderPublicationLookupResult lookup,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lookup.ExternalProviderMessageId))
        {
            return await RequireManualAsync(
                claim.Publication,
                "provider_lookup_invalid_exact_match",
                null,
                GetUtcNow(),
                cancellationToken);
        }

        try
        {
            claim.Publication.MarkProviderQueued(
                claim.LeaseToken,
                claim.PublicationFence,
                lookup.ExternalProviderMessageId,
                GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            return WebhookProviderReconciliationResult.LeaseLost();
        }

        return await PersistAsync(
            claim.Publication,
            WebhookProviderReconciliationOutcome.ProviderQueued,
            cancellationToken);
    }

    private async Task<WebhookProviderReconciliationResult> ScheduleRetryAsync(
        WebhookProviderPublicationClaim claim,
        CancellationToken cancellationToken)
    {
        var confirmedAt = GetUtcNow();
        var nextActionAt = confirmedAt.AddSeconds(_settings.ReconciliationRetryDelaySeconds);
        if (nextActionAt >= claim.Publication.IdempotencyValidUntil)
        {
            return await RequireManualAsync(
                claim.Publication,
                "provider_idempotency_expired",
                null,
                confirmedAt,
                cancellationToken);
        }

        try
        {
            claim.Publication.ScheduleRetryAfterConfirmedProviderAbsence(
                claim.LeaseToken,
                claim.PublicationFence,
                nextActionAt,
                confirmedAt);
        }
        catch (InvalidOperationException)
        {
            return WebhookProviderReconciliationResult.LeaseLost();
        }

        return await PersistAsync(
            claim.Publication,
            WebhookProviderReconciliationOutcome.RetryScheduled,
            cancellationToken);
    }

    private async Task<WebhookProviderReconciliationResult> DeferOrRequireManualAsync(
        WebhookProviderPublicationClaim claim,
        SvixProviderPublicationLookupResult lookup,
        CancellationToken cancellationToken)
    {
        var observedAt = GetUtcNow();
        var nextActionAt = observedAt.AddSeconds(_settings.ReconciliationRetryDelaySeconds);
        if (claim.Publication.AutomaticReconciliationAttemptCount >=
                _settings.MaxAutomaticReconciliationAttempts ||
            nextActionAt >= claim.Publication.IdempotencyValidUntil)
        {
            return await RequireManualAsync(
                claim.Publication,
                "provider_reconciliation_exhausted",
                lookup.FailureCategory,
                observedAt,
                cancellationToken);
        }

        try
        {
            claim.Publication.RecordAutomaticReconciliationUnresolved(
                claim.LeaseToken,
                claim.PublicationFence,
                lookup.FailureCategory ?? "provider_lookup_unavailable",
                null,
                nextActionAt,
                observedAt);
        }
        catch (InvalidOperationException)
        {
            return WebhookProviderReconciliationResult.LeaseLost();
        }

        return await PersistAsync(
            claim.Publication,
            WebhookProviderReconciliationOutcome.Deferred,
            cancellationToken);
    }

    private async Task<WebhookProviderReconciliationResult> RequireManualAsync(
        WebhookProviderPublication publication,
        string failureCategory,
        string? safeDetail,
        DateTime requiredAt,
        CancellationToken cancellationToken)
    {
        publication.RequireManualReconciliation(failureCategory, safeDetail, requiredAt);
        return await PersistAsync(
            publication,
            WebhookProviderReconciliationOutcome.ManualReconciliation,
            cancellationToken);
    }

    private async Task<WebhookProviderReconciliationResult> PersistAsync(
        WebhookProviderPublication publication,
        WebhookProviderReconciliationOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            await publicationRepository.UpdateAsync(publication, cancellationToken);
            return new WebhookProviderReconciliationResult(outcome);
        }
        catch (WebhookProviderPublicationConcurrencyException)
        {
            return WebhookProviderReconciliationResult.LeaseLost();
        }
    }

    private async Task<SvixProviderPublicationLookupRequest?> CreateLookupRequestAsync(
        WebhookProviderPublication publication,
        CancellationToken cancellationToken)
    {
        var message = await messageRepository.GetByTenantAndIdAsync(
            publication.TenantId,
            publication.WebhookMessageId,
            cancellationToken);
        if (message is null ||
            message.Id != publication.WebhookMessageId ||
            message.TenantId != publication.TenantId ||
            !string.Equals(message.PayloadHash, publication.RequestHash, StringComparison.Ordinal))
        {
            return null;
        }

        return new SvixProviderPublicationLookupRequest(
            publication.TenantId,
            publication.ProviderApplicationId,
            publication.ApplicationUid,
            publication.ProviderEnvironment,
            publication.ProviderVersion,
            publication.CredentialReference,
            publication.CredentialVersion,
            message.EventType,
            publication.ProviderEventId,
            publication.RequestHash,
            publication.PreparedAt,
            publication.IdempotencyValidUntil,
            _settings.ReconciliationLookupPageLimit);
    }

    private static bool ClaimIsActive(WebhookProviderPublicationClaim claim, DateTime observedAt) =>
        claim.Publication.Status == WebhookProviderPublicationStatus.PublicationUnknown &&
        claim.Publication.ProcessingLeaseToken == claim.LeaseToken &&
        claim.Publication.PublicationFence == claim.PublicationFence &&
        claim.LeaseExpiresAt > observedAt;

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}

public sealed record WebhookProviderReconciliationResult(WebhookProviderReconciliationOutcome Outcome)
{
    public static WebhookProviderReconciliationResult LeaseLost() =>
        new(WebhookProviderReconciliationOutcome.LeaseLost);
}

public enum WebhookProviderReconciliationOutcome
{
    ProviderQueued = 1,
    RetryScheduled = 2,
    Deferred = 3,
    ManualReconciliation = 4,
    LeaseLost = 5
}
