using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationProviderSubmissionEffectRepository(
    ExploreDbContext dbContext,
    TimeProvider timeProvider) : IRegistrationProviderSubmissionEffectRepository
{
    public Task<bool> HasCustodyAsync(
        RegistrationProviderSubmissionEffectCustody custody,
        CancellationToken cancellationToken) =>
        ActiveCustody(custody, timeProvider.GetUtcNow().UtcDateTime).AnyAsync(cancellationToken);

    public Task<RegistrationSubmissionPersistenceResult> PersistAcceptedAsync(
        RegistrationProviderSubmissionEffectCustody custody,
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        Guid expectedAttemptConcurrencyStamp,
        IReadOnlyCollection<RegistrationAnswer> answers,
        IReadOnlyCollection<RegistrationConsentRecord> consentRecords,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        IReadOnlyCollection<RegistrationRequirementFulfillment> fulfillments,
        CancellationToken cancellationToken) =>
        new RegistrationSubmissionRepository(dbContext).PersistAcceptedCoreAsync(
            attempt, submission, expectedAttemptConcurrencyStamp, answers, consentRecords, issues, fulfillments,
            cancellationToken, null, token => FenceAsync(custody, submission, token));

    public Task<RegistrationSubmissionPersistenceResult> PersistEvidenceAsync(
        RegistrationProviderSubmissionEffectCustody custody,
        RegistrationSubmission submission,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        CancellationToken cancellationToken) =>
        new RegistrationSubmissionRepository(dbContext).PersistEvidenceCoreAsync(
            submission, issues, cancellationToken, token => FenceAsync(custody, submission, token));

    private async Task<bool> FenceAsync(
        RegistrationProviderSubmissionEffectCustody custody,
        RegistrationSubmission submission,
        CancellationToken cancellationToken)
    {
        if (submission.TenantId != custody.Claim.TenantId ||
            submission.RegistrationProviderBindingId is not { } bindingId ||
            !custody.ProviderMessageId.StartsWith(bindingId.ToString("N") + ":", StringComparison.Ordinal) ||
            custody.PayloadSha256 != "sha256:" + Convert.ToHexString(
                Convert.FromBase64String(submission.ReceivedEvidenceHash.Value)).ToLowerInvariant())
        {
            return false;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        // A conditional write holds the original effect row through the owning
        // submission transaction. Check again after normalization saves so time
        // spent in persistence cannot extend the original absolute lease.
        if (await ActiveCustody(custody, now).ExecuteUpdateAsync(
                setters => setters.SetProperty(pointer => pointer.UpdatedAt, now), cancellationToken) != 1)
        {
            return false;
        }

        // Lock the retained row as well: retention or source replacement cannot
        // race between the final identity comparison and the submission commit.
        if (await dbContext.IncomingWebhookMessages.Where(message =>
                message.TenantId == custody.Claim.TenantId &&
                message.Id == custody.IncomingWebhookMessageId &&
                message.Provider == custody.Provider &&
                message.ProviderMessageId == custody.ProviderMessageId &&
                message.EventType == custody.SourceEventType &&
                message.PayloadHash == custody.PayloadSha256 &&
                message.PayloadClearedAt == null &&
                message.PayloadByteLength > 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.UpdatedAt, now), cancellationToken) != 1)
        {
            return false;
        }

        return await HasCustodyAsync(custody, cancellationToken);
    }

    private IQueryable<IncomingWebhookEffectOutbox> ActiveCustody(
        RegistrationProviderSubmissionEffectCustody custody,
        DateTime observedAt)
    {
        IncomingWebhookEffectClaim claim = custody.Claim;
        bool selectedTenant = claim.TenantId != Guid.Empty && claim.EffectOutboxId != Guid.Empty &&
            claim.LeaseToken != Guid.Empty && claim.ProcessingFence > 0 && claim.ProcessingGeneration > 0 &&
            custody.LeaseExpiresAt.Kind == DateTimeKind.Utc &&
            !dbContext.IsTenantFilterBypassed && dbContext.BindTenantLease() == claim.TenantId;
        return dbContext.IncomingWebhookEffectOutboxes.Where(pointer =>
            selectedTenant &&
            pointer.TenantId == claim.TenantId &&
            pointer.Id == claim.EffectOutboxId &&
            pointer.Status == OutboxMessageStatus.Processing &&
            pointer.ProcessingLeaseToken == claim.LeaseToken &&
            pointer.ProcessingFence == claim.ProcessingFence &&
            pointer.ProcessingGeneration == claim.ProcessingGeneration &&
            pointer.ProcessingLeaseExpiresAt == custody.LeaseExpiresAt &&
            pointer.ProcessingLeaseExpiresAt > observedAt &&
            pointer.IncomingWebhookMessageId == custody.IncomingWebhookMessageId &&
            pointer.Provider == custody.Provider &&
            pointer.ProviderDecisionId == custody.ProviderMessageId &&
            pointer.EffectKind == custody.EffectKind &&
            pointer.PayloadSha256 == custody.PayloadSha256 &&
            pointer.IncomingWebhookMessage != null &&
            pointer.IncomingWebhookMessage.TenantId == claim.TenantId &&
            pointer.IncomingWebhookMessage.Id == custody.IncomingWebhookMessageId &&
            pointer.IncomingWebhookMessage.Provider == custody.Provider &&
            pointer.IncomingWebhookMessage.ProviderMessageId == custody.ProviderMessageId &&
            pointer.IncomingWebhookMessage.EventType == custody.SourceEventType &&
            pointer.IncomingWebhookMessage.PayloadHash == custody.PayloadSha256 &&
            pointer.IncomingWebhookMessage.PayloadClearedAt == null &&
            pointer.IncomingWebhookMessage.PayloadByteLength > 0);
    }
}
