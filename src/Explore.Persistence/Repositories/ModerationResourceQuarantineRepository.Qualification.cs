using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineRepository
{
    private async Task<QualifiedCause?> QualifyAsync(
        Guid effectId, Guid leaseId, long revision, int ordinal,
        Guid resourceId, Guid materialVersionId, CancellationToken token)
    {
        var authority = await new ModerationMaintenanceAuthority(context, machine, bindings, keys, clock)
            .AcquireForEffectAsync(effectId, leaseId, revision, privacy, token);
        if (authority is null) return null;
        var key = machine.Current is { } principal
            ? await keys.GetByKeyIdForAuthentication(principal.KeyId, token) : null;
        if (key is null || !await context.Tenants.AsNoTracking().AnyAsync(
            row => row.Id == authority.Tenant.Id && row.TenantStatus.IsActiveState, token))
            return null;
        var observedAt = clock.GetUtcNow();
        // AcquireForEffectAsync checks metadata-only ownership/R before touching the private
        // source. Its transaction-owned effect and notice fences remain held throughout this read.
        var ownership = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == effectId).Select(row => new { row.TenantId, row.RetainUntil })
            .SingleOrDefaultAsync(token);
        if (ownership is null || ownership.TenantId != authority.Tenant.Id
            || ownership.RetainUntil is not { } deadline || clock.GetUtcNow() >= deadline)
            return null;
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effectId, token);
        if (source is null || effect is null || !source.Matches(effect)
            || source.TenantId != authority.Tenant.Id || source.RetainUntil is not { } retainedUntil
            || observedAt >= retainedUntil)
            return null;
        ModerationNoticeCaptureSnapshot? capture;
        try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return null; }
        if (capture is null || !capture.Matches(effect) || capture.HasNoSubstantiveWork
            || capture.Notice is not { } notice || capture.ValidUntil is not { } captureUntil
            || captureUntil.Offset != TimeSpan.Zero || captureUntil == DateTimeOffset.MaxValue
            || capture.Finding is not { State: ModerationFindingState.Final } originalFinding
            || capture.Decision.State != ModerationDecisionState.Active || capture.Decision.ApprovalCount < 1
            || notice.DecisionState != capture.Decision.State || notice.SanctionState != ModerationSanctionState.Active
            || effect.OperationKind is not ((int)ModerationOperationKind.Activate)
                and not ((int)ModerationOperationKind.Reconcile) and not ((int)ModerationOperationKind.Supersede)
            || notice.OperationKind != effect.OperationKind || notice.ReferenceProfile != ModerationReferenceProfileId.CFG09
            || notice.SubjectKind != ModerationSubjectKind.Content || capture.Decision.SubjectKind != notice.SubjectKind
            || notice.SubjectId != capture.Decision.SubjectId || notice.Scope != capture.Decision.Scope
            || notice.SubjectOrigin != capture.Decision.SubjectOrigin || notice.Rule != capture.Decision.Rule
            || capture.HostBindingId != authority.Binding.Id || capture.HostSolutionId != authority.Binding.SolutionId
            || capture.HostInstanceId != authority.Binding.InstanceId || capture.WriterTenantId != authority.Tenant.Id
            || capture.AuthoritySourceId != authority.Epoch.Id || capture.QualificationRevision > authority.Epoch.Revision
            || notice.RetainUntil != retainedUntil || retainedUntil == DateTimeOffset.MaxValue
            || observedAt < source.CapturedAt || observedAt >= captureUntil || notice.Effects.IsDefaultOrEmpty
            || notice.Consequences.IsDefaultOrEmpty || ordinal >= notice.Effects.Length
            || effect.SanctionId is not { } sanctionId)
            return null;
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effect.DecisionId, token);
        if (decision is null || decision.CaseId != capture.Decision.CaseId
            || decision.Subject.Kind != ModerationSubjectKind.Content || decision.Subject.Id != notice.SubjectId
            || decision.Subject.Origin != authority.Tenant || decision.Scope.Kind != ModerationScopeKind.Resource
            || decision.Scope.Parent != authority.Tenant || decision.Scope.Id != resourceId
            || ModerationNoticeScope.From(decision.Scope) != notice.Scope
            || ModerationNoticeScope.From(decision.Subject.Origin) != notice.SubjectOrigin
            || notice.Rule != new ModerationNoticeRule(decision.Rule.OwnerId, decision.Rule.Code, decision.Rule.Revision)
            || decision.CapabilityMask != capture.Decision.CapabilityMask
            || decision.StartsAt != capture.Decision.StartsAt || decision.EndsAt != capture.Decision.EndsAt
            || decision.ReviewAt != capture.Decision.ReviewAt)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, decision.CaseId, token);
        var moderationCase = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == decision.CaseId, token);
        var finding = moderationCase is null ? null : await ModerationOriginalFindingProofOwner.AcquireAsync(
            context, moderationCase, originalFinding, decision.Scope, authority.Binding, source.CapturedAt, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationSanction>(context, sanctionId, token);
        var sanction = await context.ModerationSanctions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sanctionId, token);
        if (moderationCase is null || moderationCase.Subject != decision.Subject
            || finding is null || finding.Id != decision.FindingId || finding.Revision != originalFinding.Revision
            || finding.State != ModerationFindingState.Final || finding.CaseId != decision.CaseId
            || finding.Subject != decision.Subject || finding.Scope != decision.Scope || finding.Rule != decision.Rule
            || originalFinding.Reason != new ModerationNoticeRule(finding.Reason.OwnerId, finding.Reason.Code, finding.Reason.Revision)
            || sanction is null || sanction.DecisionId != decision.Id || sanction.Subject != decision.Subject
            || sanction.Scope != decision.Scope || sanction.Rule != decision.Rule || !sanction.IsEffectiveAt(observedAt)
            || sanction.Revision != effect.SanctionRevision || sanction.StartsAt != capture.Decision.StartsAt
            || sanction.EndsAt != capture.Decision.EndsAt || sanction.ReviewAt != capture.Decision.ReviewAt)
            return null;

        await using var selectionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.contribution:{authority.Binding.Id:N}:{effectId:N}", token);
        var selected = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
        if (selected is null || authority.NativeTargets is not { } targetPlan
            || !await targetPlan.MatchesAsync(context, selected, token)) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, selected.Id, token);
        selected.ValidatePersistedState();
        if (selected.HostBindingId != authority.Binding.Id || selected.TenantId != authority.Tenant.Id
            || selected.TrustDomainId != effect.TrustDomainId || selected.DecisionId != decision.Id
            || selected.DecisionRevision != effect.DecisionRevision || selected.CaseId != decision.CaseId
            || selected.CaseRevision != moderationCase.Revision || selected.PolicyId != notice.PolicyId
            || selected.PolicyRevision != effect.PolicyRevision || selected.PublicationId != notice.PublicationId
            || selected.ProfileId != notice.ProfileId || selected.DecisionId != notice.ContributionId
            || selected.StartsAt != capture.Decision.StartsAt || selected.RecordedAt > source.CapturedAt
            || selected.ValidUntil <= observedAt || selected.WriterGeneration > effect.QualificationRevision
            || !Administrator(selected.AuthoredByHumanId))
            return null;
        var contribution = ModerationContributionSelectionProjection.Restore(selected, decision);
        if (ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selected.SelectionDigest
            || !await ReceiptAsync(new RecordModerationContributionSelectionSource
            {
                OperationId = Guid.Empty,
                Target = decision.Scope,
                ExpectedQualificationRevision = selected.WriterGeneration - 1,
                SelectionId = selected.Id,
                EffectId = effectId,
                CaseId = selected.CaseId,
                ExpectedCaseRevision = selected.CaseRevision,
                DecisionId = decision.Id,
                ExpectedDecisionRevision = selected.DecisionRevision,
                PolicyId = selected.PolicyId,
                ExpectedPolicyRevision = selected.PolicyRevision,
                PublicationId = selected.PublicationId,
                ProfileId = selected.ProfileId,
                Contribution = contribution,
                ValidUntil = selected.ValidUntil
            }, ModerationQualificationSourceKind.ContributionSelection, selected.Id, selected.Revision,
                selected.AuthoredByHumanId, selected.WriterGeneration, selected.RecordedAt, authority, token))
            return null;

        // Read the exact immutable original publication, never today's selection or catalogue as
        // a replacement grant. RestoreHistory rejects unsupported historical catalogue versions.
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPolicyPublication>(context, notice.PublicationId, token);
        var publication = await ModerationPolicyPublicationGraph.Include(context.ModerationPolicyPublications.AsNoTracking())
            .SingleOrDefaultAsync(row => row.Id == notice.PublicationId, token);
        if (publication is null || publication.HostBindingId != authority.Binding.Id
            || publication.TrustDomainId != effect.TrustDomainId || publication.TenantId != authority.Tenant.Id
            || publication.PolicyId != notice.PolicyId || publication.Revision != effect.PolicyRevision
            || publication.CatalogueRevision != notice.CatalogueRevision
            || !publication.Jurisdiction.Contains(decision.Scope) || publication.PublishedAt > source.CapturedAt
            || notice.ProfileSource != new ModerationNoticeSourceStamp(
                publication.Id, publication.Revision, publication.WriterGeneration))
            return null;
        var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication);
        if (draft is null || !draft.Profiles.Any(row => row.Definition.Id == selected.ProfileId
            && row.Definition.Rule == decision.Rule && row.Definition.ReferenceProfile == notice.ReferenceProfile
            && row.Definition.Reason == finding.Reason && row.Definition.ReferenceRevision == notice.ReferenceRevision
            && row.Definition.RequiredApprovals <= capture.Decision.ApprovalCount))
            return null;
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(draft, [contribution], source.CapturedAt);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview
            || composed.Effects.Length != notice.Effects.Length || composed.Consequences.Length != notice.Consequences.Length
            || !composed.Effects.Zip(notice.Effects).All(pair => pair.First.Effect == pair.Second.Effect
                && pair.First.Interval == pair.Second.Interval && TargetMatches(pair.First.Target, pair.Second.Target))
            || !composed.Consequences.Zip(notice.Consequences).All(pair => pair.First.Plan == pair.Second.Plan
                && pair.First.Strategy == pair.Second.Strategy && pair.First.ProtectionPurpose == pair.Second.ProtectionPurpose
                && pair.First.Interval == pair.Second.Interval && TargetMatches(pair.First.Target, pair.Second.Target)))
            return null;
        var original = notice.Effects[ordinal];
        var binding = selected.Bindings.SingleOrDefault(row => row.Ordinal == ordinal);
        if (binding is null || binding.Kind != ModerationContributionBindingKind.Effect || binding.Effect != original.Effect
            || original.Effect is not
            {
                Kind: ModerationReferenceEffectKind.UnsafeResourceVersion,
                Mode: ModerationReferenceEffectMode.Quarantine, Target: ModerationReferenceTarget.SelectedResourceVersion,
                Capability: ModerationCapability.ReadResource, Execution: ModerationReferenceExecution.NativeOwner
            }
            || original.Interval.Kind != ModerationReferenceTimingKind.ObjectReviewWithinDay
            || original.Interval.ReviewAt is null
            || !original.Interval.IsEffectiveAt(observedAt) || original.Interval.StartsAt != capture.Decision.StartsAt
            || original.Interval.EndsAt != capture.Decision.EndsAt || original.Interval.ReviewAt != capture.Decision.ReviewAt
            || binding.Scope != decision.Scope || binding.TargetId != resourceId
            || binding.SubjectKind != decision.Subject.Kind || binding.SubjectId != decision.Subject.Id
            || original.Target.Owner != "event.resource-generation"
            || !notice.Consequences.Any(row => row.Strategy is null
                && row.Plan.Content == ModerationContentMode.QuarantineNamedVersion
                && row.Plan.Resources == ModerationResourceReadMode.QuarantineNamedVersionSafeStatusOnly
                && row.Target == original.Target && row.Interval == original.Interval))
            return null;

        var profileUntil = await ProfileAsync(selected, decision, draft, authority, effect, observedAt, token);
        if (profileUntil is null) return null;
        await using var adoptionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.resource-target:{selected.Id:N}:{ordinal}", token);
        var adoption = await context.ModerationResourceTargetAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SelectionId == selected.Id && row.BindingOrdinal == ordinal, token);
        if (adoption is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceTargetAdoption>(context, adoption.Id, token);
        adoption.ValidatePersistedState();
        var generation = await context.ModerationResourceGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == adoption.GenerationId, token);
        if (generation is null || targetPlan.Resource(selected.Id, adoption, generation) is not { } ancestryPlan) return null;
        generation.ValidatePersistedState();
        if (adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != effect.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || adoption.SelectionRevision != selected.Revision
            || adoption.GenerationRevision != generation.Revision || adoption.GenerationWriterGeneration != generation.WriterGeneration
            || adoption.CaseId != selected.CaseId || adoption.CaseRevision != selected.CaseRevision
            || !Administrator(adoption.AdoptedByHumanId) || adoption.AdoptedByHumanId == selected.AuthoredByHumanId
            || adoption.AdoptedByHumanId == decision.IssuerId || adoption.AdoptedByHumanId == decision.Subject.Id
            || adoption.RevokedAt is not null || adoption.Revision != 1 || adoption.AdoptedAt > source.CapturedAt
            || adoption.ValidUntil <= observedAt || adoption.ValidUntil > generation.ValidUntil
            || adoption.ValidUntil > selected.ValidUntil || adoption.WriterGeneration > effect.QualificationRevision
            || generation.HostBindingId != authority.Binding.Id || generation.TrustDomainId != effect.TrustDomainId
            || generation.TenantId != authority.Tenant.Id || generation.ResourceId != resourceId
            || generation.MaterialVersionId != materialVersionId || generation.EventId != decision.Subject.Id
            || generation.Revision != binding.TargetRevision || generation.ObservedAt > source.CapturedAt
            || generation.ValidUntil <= observedAt || generation.WriterGeneration > effect.QualificationRevision
            || !Administrator(generation.ObservedByHumanId)
            || original.Target.Source != new ModerationNoticeSourceStamp(generation.Id, generation.Revision,
                generation.WriterGeneration.ToString(CultureInfo.InvariantCulture)))
            return null;

        var receipt = await context.EventResourceModerationQuarantines.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effectId && row.BindingOrdinal == ordinal, token);
        if (receipt is not null && (receipt.TenantId != authority.Tenant.Id || receipt.EventResourceId != resourceId
            || receipt.EventId != generation.EventId || receipt.MaterialVersionId != materialVersionId
            || receipt.AppliedAt < effect.CreatedAt || receipt.AppliedAt > observedAt))
            return null;
        // A prior exact native cause changes the resource's concurrency stamp itself. Only that
        // replay may ignore the stamp; material, ancestry, storage and current adoption still match.
        var native = await ModerationResourceGenerationReader.AcquireAsync(context, generation.Id, resourceId,
            generation.HostBindingId, generation.TrustDomainId, generation.TenantId, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt,
            generation.ValidUntil, token, generation, replay: receipt, ancestryPlan: ancestryPlan);
        if (native is null || native.EventStamp != generation.EventStamp || native.ActorStamp != generation.ActorStamp
            || native.OrganizerActorStamp != generation.OrganizerActorStamp)
            return null;
        await using var generationSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.resource-generation:{authority.Binding.Id:N}:{resourceId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceGeneration>(context, generation.Id, token);
        if (await context.ModerationResourceGenerations.AsNoTracking().AnyAsync(row =>
            row.HostBindingId == authority.Binding.Id && row.ResourceId == resourceId && row.Revision > generation.Revision, token)
            || !await ReceiptAsync(new RecordModerationResourceGenerationSource
            {
                OperationId = Guid.Empty,
                Target = authority.Tenant,
                SourceId = generation.Id,
                ResourceId = resourceId,
                ExpectedResourceRevision = generation.Revision - 1,
                ExpectedQualificationRevision = generation.WriterGeneration - 1,
                ValidUntil = generation.ValidUntil
            }, ModerationQualificationSourceKind.ResourceGeneration, generation.Id, generation.Revision,
                generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, authority, token)
            || !await ReceiptAsync(new AdoptModerationResourceTargetSource
            {
                OperationId = Guid.Empty,
                Target = decision.Scope,
                ExpectedQualificationRevision = adoption.WriterGeneration - 1,
                AdoptionId = adoption.Id,
                SelectionId = selected.Id,
                BindingOrdinal = ordinal,
                GenerationId = generation.Id,
                ExpectedGenerationRevision = generation.Revision,
                CaseId = adoption.CaseId,
                ExpectedCaseRevision = adoption.CaseRevision,
                ValidUntil = adoption.ValidUntil
            }, ModerationQualificationSourceKind.ResourceTargetAdoption, adoption.Id, 1,
                adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, authority, token))
            return null;
        var until = new[] { retainedUntil, captureUntil, selected.ValidUntil, generation.ValidUntil,
            adoption.ValidUntil, profileUntil.Value, effect.LeaseExpiresAt!.Value,
            original.Interval.ReviewAt ?? DateTimeOffset.MaxValue, binding.WindowEnd ?? DateTimeOffset.MaxValue,
            key.ExpiresAt is { } expiresAt
                ? new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)) : DateTimeOffset.MaxValue }.Min();
        var now = clock.GetUtcNow();
        return now >= observedAt && now < until ? new(effect, generation, observedAt, until) : null;

        bool Administrator(Guid id) => authority.FencedHumans.Contains(id) && authority.Administrators.Contains(id);
    }

    private static bool TargetMatches(ModerationPolicyTarget target, ModerationNoticeTarget original) =>
        ModerationNoticeScope.From(target.Scope) == original.Scope && target.SubjectKind == original.SubjectKind
        && target.SubjectId == original.SubjectId && target.TargetId == original.TargetId
        && target.TargetRevision == original.TargetRevision && target.Operation == original.Operation
        && target.WindowEnd == original.WindowEnd;

    private async Task<bool> ReceiptAsync(
        ModerationQualificationMutation template, ModerationQualificationSourceKind kind, Guid id, long revision,
        Guid humanId, long generation, DateTimeOffset recordedAt, ModerationSourceAuthority authority, CancellationToken token)
    {
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SourceKind == kind && row.SourceId == id && row.ResultRevision == revision, token);
        if (receipt is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receipt.Id, token);
        receipt.ValidatePersistedState();
        var request = template with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.RequestDigest == digest && receipt.HostBindingId == authority.Binding.Id
            && receipt.TrustDomainId == authority.Binding.TrustDomainId && receipt.TenantId == authority.Tenant.Id
            && receipt.PrincipalId == humanId && receipt.QualificationRevision == generation
            && receipt.CompletedAt == recordedAt && generation <= authority.Epoch.Revision;
    }

    private async Task<DateTimeOffset?> ProfileAsync(
        ModerationContributionSelection selected, ModerationDecision decision, ModerationPolicyCompiledDraft draft,
        ModerationSourceAuthority authority, ModerationEffectIntent effect, DateTimeOffset observedAt, CancellationToken token)
    {
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.profile-adoption:{selected.Id:N}", token);
        var adoption = await context.ModerationProfileAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SelectionId == selected.Id, token);
        if (adoption is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileAdoption>(context, adoption.Id, token);
        adoption.ValidatePersistedState();
        var gates = draft.Prerequisites.SingleOrDefault(row =>
            row.Rule == decision.Rule && row.Mode == ModerationNativeMode.Embedded)?.ActivationGates;
        if (gates is null || adoption.ModeCode != (int)ModerationNativeMode.Embedded
            || adoption.PrerequisiteMask != gates.Value.Aggregate(0, (mask, gate) => mask | 1 << ((int)gate - 1))
            || adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != effect.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || adoption.SelectionRevision != selected.Revision
            || adoption.SelectionWriterGeneration != selected.WriterGeneration || adoption.PublicationId != selected.PublicationId
            || adoption.ProfileId != selected.ProfileId || adoption.CaseId != selected.CaseId
            || adoption.CaseRevision != selected.CaseRevision || adoption.Revision != 1 || adoption.RevokedAt is not null
            || adoption.AdoptedAt > effect.CreatedAt || adoption.ValidUntil <= observedAt
            || adoption.ValidUntil > selected.ValidUntil || adoption.WriterGeneration > effect.QualificationRevision
            || adoption.AdoptedByHumanId == selected.AuthoredByHumanId || adoption.AdoptedByHumanId == decision.IssuerId
            || adoption.AdoptedByHumanId == decision.Subject.Id || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId)
            || !authority.Administrators.Contains(adoption.AdoptedByHumanId)
            || !await ReceiptAsync(new AdoptModerationProfileSource
            {
                OperationId = Guid.Empty,
                Target = decision.Scope,
                ExpectedQualificationRevision = adoption.WriterGeneration - 1,
                AdoptionId = adoption.Id,
                SelectionId = selected.Id,
                Mode = ModerationNativeMode.Embedded,
                CaseId = adoption.CaseId,
                ExpectedCaseRevision = adoption.CaseRevision,
                ValidUntil = adoption.ValidUntil
            }, ModerationQualificationSourceKind.ProfileAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, authority, token))
            return null;
        return adoption.ValidUntil;
    }
}
