using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationOriginalEffectCompletionRepository
{
    private async Task<DateTimeOffset> ConsumersAsync(ModerationConsumerEvidenceSession session,
        ModerationPolicyCompiledDraft policy, ModerationDecision decision,
        ImmutableArray<ModerationActivationPrerequisite> prerequisites,
        DateTimeOffset observedAt, DateTimeOffset until, List<string> facts, CancellationToken token)
    {
        var binding = new ModerationActivationBinding(policy.Publication.PolicyId, policy.Publication.ProposedRevision,
            policy.Publication.PublicationId, policy.Publication.Jurisdiction, decision.Rule,
            ModerationCapability.ReadResource, ModerationNativeMode.Embedded);
        var registrations = new List<ModerationCapabilityRegistration>();
        var currentSources = new List<ModerationCapabilitySourceObservation>();
        // Never intersect with what the host happens to implement. The real source currently
        // lacks non-payload measurement producers; their absence is retryable pending, not success.
        foreach (var consumer in inventory.RequiredConsumers(binding.Capability, binding.Mode))
        {
            var current = await sources.AcquireConsumerAsync(session, session.TransactionId, binding, consumer, observedAt, token);
            var registration = await sources.ReadConformanceAsync(session, session.TransactionId, binding, consumer, observedAt, token);
            if (current is null || registration is null)
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.TerminalConsumerProof);
            currentSources.Add(current);
            registrations.Add(registration);
        }
        if (!inventory.Match(binding, registrations, currentSources, observedAt).Matches)
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.TerminalConsumerProof);
        foreach (var registration in registrations)
        {
            until = new[] { until, registration.ValidUntil,
                currentSources.Single(row => row.ConsumerId == registration.Contract.Id).ValidUntil }.Min();
            facts.Add(Digest(new { registration.Binding, registration.Contract, registration.InventoryFingerprint,
                registration.SourceId, registration.SourceRevision, registration.WriterGeneration, registration.ValidUntil }));
        }
        var approvals = new HashSet<Guid>();
        var sourceIds = new HashSet<Guid>();
        foreach (var prerequisite in prerequisites.Order())
        {
            var proof = await sources.AcquirePrerequisiteAsync(
                session, session.TransactionId, binding, prerequisite, observedAt, token);
            if (proof is null || proof.TransactionId != session.TransactionId || proof.Binding != binding
                || proof.Prerequisite != prerequisite || proof.ObservedAt != observedAt
                || proof.ApprovalId == proof.SourceId || proof.ApprovalId == Guid.Empty || proof.SourceId == Guid.Empty
                || proof.ApprovalRevision < 1 || proof.ApprovedSourceRevision < 1
                || proof.ApprovedSourceRevision != proof.CurrentSourceRevision
                || string.IsNullOrWhiteSpace(proof.ApprovedWriterGeneration)
                || proof.ApprovedWriterGeneration != proof.CurrentWriterGeneration
                || proof.AdoptedAt > observedAt || proof.ValidUntil <= observedAt
                || !approvals.Add(proof.ApprovalId) || !sourceIds.Add(proof.SourceId))
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.NativePurposeProof);
            until = until < proof.ValidUntil ? until : proof.ValidUntil;
            facts.Add(Digest(new { proof.Binding, proof.Prerequisite, proof.ApprovalId, proof.ApprovalRevision,
                proof.SourceId, proof.CurrentSourceRevision, proof.CurrentWriterGeneration, proof.AdoptedAt, proof.ValidUntil }));
        }
        return until;
    }
}
