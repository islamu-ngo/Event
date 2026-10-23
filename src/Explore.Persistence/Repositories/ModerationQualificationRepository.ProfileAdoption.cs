using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<QualifiedSource> AdoptProfileAsync(
        AdoptModerationProfileSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == source.SelectionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        selection.ValidatePersistedState();
        if (selection.HostBindingId != authority.Binding.Id || selection.TrustDomainId != authority.Binding.TrustDomainId
            || selection.TenantId != authority.Tenant.Id || selection.CaseId != moderationCase.Id
            || selection.CaseRevision != moderationCase.Revision || selection.AuthoredByHumanId == authority.UserId
            || !authority.FencedHumans.Contains(selection.AuthoredByHumanId)
            || !authority.Administrators.Contains(selection.AuthoredByHumanId) || selection.RecordedAt > now
            || selection.ValidUntil <= now || source.ValidUntil <= now || source.ValidUntil > selection.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, selection.DecisionId, token);
        var decision = await context.ModerationDecisions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == selection.DecisionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        decision.ValidatePersistedState();
        if (decision.CaseId != selection.CaseId || decision.Scope != source.Target || decision.Subject != moderationCase.Subject
            || decision.IssuerId == authority.UserId || decision.Subject.Id == authority.UserId
            || decision.IssuerKind == ModerationSubjectKind.Human && !authority.FencedHumans.Contains(decision.IssuerId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (selection.DecisionRevision != decision.Revision
            && (decision.Revision >= long.MaxValue - 1 || selection.DecisionRevision != decision.Revision + 1))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        AuthorityScope? jurisdiction = source.Target;
        while (jurisdiction is not null && jurisdiction.Id != decision.Rule.OwnerId) jurisdiction = jurisdiction.Parent;
        if (jurisdiction is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var publication = await publications.AcquireSelectedAsync(context.Database.CurrentTransaction!.TransactionId,
            authority.Binding.Id, jurisdiction, selection.PolicyId, false, token);
        if (publication is null || publication.Id != selection.PublicationId || publication.Revision != selection.PolicyRevision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var policy = ModerationPolicyPublicationProjection.RestoreHistory(publication);
        var contribution = ModerationContributionSelectionProjection.Restore(selection, decision);
        if (policy is null || !policy.Proposed.Modes.Contains(source.Mode)
            || !policy.Profiles.Any(row => row.Definition.Id == selection.ProfileId && row.Definition.Rule == decision.Rule)
            || ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selection.SelectionDigest)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(policy, [contribution], now);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Effects.IsEmpty || composed.Consequences.IsEmpty
            || composed.Effects.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt)
            || composed.Consequences.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var gates = policy.Prerequisites.Single(row => row.Rule == decision.Rule && row.Mode == source.Mode).ActivationGates;
        var mask = gates.Aggregate(0, (value, gate) => value | 1 << ((int)gate - 1));
        await using var selectionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.contribution:{authority.Binding.Id:N}:{selection.EffectId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, selection.Id, token);
        await RequireTargetSourceReceiptAsync(new RecordModerationContributionSelectionSource
        {
            OperationId = Guid.Empty,
            Target = decision.Scope,
            ExpectedQualificationRevision = selection.WriterGeneration - 1,
            SelectionId = selection.Id,
            EffectId = selection.EffectId,
            CaseId = selection.CaseId,
            ExpectedCaseRevision = selection.CaseRevision,
            DecisionId = decision.Id,
            ExpectedDecisionRevision = selection.DecisionRevision,
            PolicyId = selection.PolicyId,
            ExpectedPolicyRevision = selection.PolicyRevision,
            PublicationId = selection.PublicationId,
            ProfileId = selection.ProfileId,
            Contribution = contribution,
            ValidUntil = selection.ValidUntil
        }, ModerationQualificationSourceKind.ContributionSelection, selection.Id, selection.Revision,
            selection.AuthoredByHumanId, selection.WriterGeneration, selection.RecordedAt, authority, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.profile-adoption:{selection.Id:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileAdoption>(context, source.AdoptionId, token);
        if (await context.ModerationProfileAdoptions.AsNoTracking().AnyAsync(
            row => row.Id == source.AdoptionId || row.SelectionId == selection.Id, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationProfileAdoption.Adopt(source.AdoptionId, selection, (int)source.Mode, mask,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, source.ValidUntil);
        context.ModerationProfileAdoptions.Add(adoption);
        return new(ModerationQualificationSourceKind.ProfileAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> RevokeProfileAsync(
        RevokeModerationProfileSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token,
            requireOpen: false, requireCurrentAttribution: false);
        var discovered = await context.ModerationProfileAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.profile-adoption:{discovered.SelectionId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileAdoption>(context, source.AdoptionId, token);
        var adoption = await context.ModerationProfileAdoptions.SingleAsync(row => row.Id == source.AdoptionId, token);
        adoption.ValidatePersistedState();
        if (adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != authority.Binding.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || adoption.CaseId != source.CaseId
            || adoption.SelectionId != discovered.SelectionId || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (adoption.Revision != source.ExpectedAdoptionRevision || adoption.RevokedAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Revoke(source.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.ProfileAdoption, adoption.Id, adoption.Revision);
    }
}
