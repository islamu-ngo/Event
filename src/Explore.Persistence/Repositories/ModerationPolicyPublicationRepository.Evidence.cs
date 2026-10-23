using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    private async Task<List<ModerationPolicyPublicationEvidence>> QualifyCompleteAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        ModerationPolicyCompiledDraft draft, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceOwner is null || context.Database.CurrentTransaction is not { } transaction
            || evidenceSession.TransactionId != transaction.TransactionId
            || draft.Publication.InventoryRevision != inventory.Revision
            || draft.Publication.InventoryFingerprint != inventory.Fingerprint
            || draft.Proposed.Modes.Any(mode => inventory.Modes[mode].SupportState != ModerationSourceSupport.ImplementedUnverified)
            || draft.Consumers.SelectMany(requirement => requirement.Consumers)
                .Any(consumer => !inventory.Consumers.TryGetValue(consumer.Id, out var entry)
                    || entry.SupportState != ModerationSourceSupport.ImplementedUnverified))
            throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
        var transactionId = transaction.TransactionId;
        var tenantId = context.TenantFilterTenantId;
        var trustDomain = context.ModerationTrustDomainId;
        var result = new List<ModerationPolicyPublicationEvidence>();
        var proposed = ModerationPolicyPublicationProjection.CanonicalProposed(draft.Proposed);
        var obligations = new List<ModerationPolicyPublicationObligation>();
        foreach (var mode in draft.Proposed.Modes.Order())
            obligations.Add(new(draft.Publication, mode, draft.Bounds, proposed));
        foreach (var requirement in draft.Prerequisites)
        {
            // Each substantive gate requires its own exact approval and independent native read;
            // a provider cannot return a passing subset of a combined list.
            foreach (var gate in requirement.ActivationGates)
                obligations.Add(new(draft.Publication, requirement.Mode, draft.Bounds, proposed,
                    requirement with { ActivationGates = [gate], NativePrerequisites = [] }));
            foreach (var prerequisite in requirement.NativePrerequisites)
                obligations.Add(new(draft.Publication, requirement.Mode, draft.Bounds, proposed,
                    requirement with { ActivationGates = [], NativePrerequisites = [prerequisite] }));
        }
        obligations.AddRange(draft.Effects.Select(effect =>
            new ModerationPolicyPublicationObligation(draft.Publication, effect.Mode, draft.Bounds, proposed, Effect: effect)));
        obligations.AddRange(draft.Consequences.Select(consequence =>
            new ModerationPolicyPublicationObligation(draft.Publication, consequence.Mode, draft.Bounds, proposed, Consequence: consequence)));
        foreach (var obligation in obligations)
        {
            var key = ModerationPolicyPublicationProjection.Digest(obligation);
            var source = await evidenceOwner.AcquireSourceAsync(evidenceSession, transactionId, obligation, observedAt, token);
            if (!Current() || source is null)
                throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
            var approval = await evidenceOwner.ReadApprovalAsync(evidenceSession, transactionId, obligation, observedAt, token);
            if (!Current() || source is null || approval is null
                || source.TransactionId != transactionId || approval.TransactionId != transactionId
                || source.RequirementDigest != key || approval.RequirementDigest != key
                || source.ObservedAt != observedAt || approval.QualifiedAt != observedAt
                || !Version7(approval.ApprovalId) || !Version7(source.SourceId)
                || approval.ApprovalId == source.SourceId || approval.ApprovalRevision < 1 || source.Revision < 1
                || approval.ApprovedSourceId != source.SourceId || approval.ApprovedSourceRevision != source.Revision
                || !Generation(source.WriterGeneration) || approval.ApprovedWriterGeneration != source.WriterGeneration
                || approval.ValidUntil <= observedAt || approval.ValidUntil.Offset != TimeSpan.Zero
                || approval.ValidUntil == DateTimeOffset.MaxValue)
                throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
            result.Add(new()
            {
                PublicationId = draft.Publication.PublicationId,
                RequirementDigest = key,
                ApprovalId = approval.ApprovalId,
                ApprovalRevision = approval.ApprovalRevision,
                SourceId = source.SourceId,
                SourceRevision = source.Revision,
                WriterGeneration = source.WriterGeneration,
                ObservedAt = observedAt,
                ValidUntil = approval.ValidUntil
            });
        }
        var validUntil = result.Min(row => row.ValidUntil);
        foreach (var requirement in draft.Consumers)
        {
            var registrations = new List<ModerationCapabilityRegistration>();
            var sources = new List<ModerationCapabilitySourceObservation>();
            // The immutable compiled exact inventory, never GetServices/available-set intersection.
            foreach (var contract in requirement.Consumers)
            {
                var source = await evidenceOwner.AcquireConsumerAsync(
                    evidenceSession, transactionId, requirement.Binding, contract.Id, observedAt, token);
                if (!Current() || source is null)
                    throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                var registration = await evidenceOwner.ReadConformanceAsync(
                    evidenceSession, transactionId, requirement.Binding, contract.Id, observedAt, token);
                if (!Current() || source is null || registration is null)
                    throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                sources.Add(source);
                registrations.Add(registration);
            }
            if (!inventory.Match(requirement.Binding, registrations, sources, observedAt).Matches)
                throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
            foreach (var registration in registrations)
                if (registration.ValidUntil < validUntil) validUntil = registration.ValidUntil;
            foreach (var source in sources)
            {
                if (source.ValidUntil < validUntil) validUntil = source.ValidUntil;
                result.Add(new()
                {
                    PublicationId = draft.Publication.PublicationId,
                    RequirementDigest = ModerationPolicyPublicationProjection.Digest(new
                    { requirement.Binding, source.ConsumerId, draft.Proposed.InventoryFingerprint }),
                    ApprovalId = null,
                    ApprovalRevision = null,
                    SourceId = source.SourceId,
                    SourceRevision = source.Revision,
                    WriterGeneration = source.WriterGeneration,
                    ObservedAt = observedAt,
                    ValidUntil = validUntil
                });
            }
        }
        if (!Current() || clock.GetUtcNow() < observedAt || clock.GetUtcNow() >= validUntil)
            throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
        return result;

        bool Current() => evidenceSession.TransactionId == transactionId && CurrentTransaction(transactionId)
            && ReferenceEquals(context.Database.CurrentTransaction, transaction)
            && context.TenantFilterTenantId == tenantId && context.ModerationTrustDomainId == trustDomain;
    }

    private static bool Generation(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && value == value.Trim()
        && !value.Any(char.IsControl);
}
