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
    private async Task<QualifiedSource> AdoptEventTargetAsync(
        AdoptModerationEventTargetSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
        var selected = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == source.SelectionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        selected.ValidatePersistedState();
        if (selected.HostBindingId != authority.Binding.Id || selected.TrustDomainId != authority.Binding.TrustDomainId
            || selected.TenantId != authority.Tenant.Id || selected.CaseId != moderationCase.Id
            || selected.CaseRevision != moderationCase.Revision || selected.AuthoredByHumanId == authority.UserId
            || !authority.FencedHumans.Contains(selected.AuthoredByHumanId)
            || !authority.Administrators.Contains(selected.AuthoredByHumanId) || selected.ValidUntil <= now
            || selected.RecordedAt > now || source.ValidUntil <= now || source.ValidUntil > selected.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, selected.DecisionId, token);
        var decision = await context.ModerationDecisions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == selected.DecisionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        decision.ValidatePersistedState();
        if (decision.CaseId != selected.CaseId || decision.Scope != source.Target || decision.Subject != moderationCase.Subject
            || decision.Subject.Kind != ModerationSubjectKind.Human
            || decision.IssuerId == authority.UserId || decision.Subject.Id == authority.UserId
            || decision.IssuerKind == ModerationSubjectKind.Human && !authority.FencedHumans.Contains(decision.IssuerId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        // Adoption retains a draft; final lifecycle capture chooses the lawful revision.
        if (decision.Revision >= long.MaxValue - 1
            || selected.DecisionRevision != decision.Revision
                && selected.DecisionRevision != decision.Revision + 1)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        AuthorityScope? jurisdiction = source.Target;
        while (jurisdiction is not null && jurisdiction.Id != decision.Rule.OwnerId) jurisdiction = jurisdiction.Parent;
        if (jurisdiction is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var publication = await publications.AcquireSelectedAsync(context.Database.CurrentTransaction!.TransactionId,
            authority.Binding.Id, jurisdiction, selected.PolicyId, false, token);
        if (publication is null || publication.Id != selected.PublicationId || publication.Revision != selected.PolicyRevision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication);
        var contribution = ModerationContributionSelectionProjection.Restore(selected, decision);
        if (draft is null || !draft.Profiles.Any(row => row.Definition.Id == selected.ProfileId && row.Definition.Rule == decision.Rule)
            || ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selected.SelectionDigest)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(draft, [contribution], now);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Consequences.IsEmpty
            || composed.Effects.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt)
            || composed.Consequences.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var binding = selected.Bindings.SingleOrDefault(row => row.Ordinal == source.BindingOrdinal);
        var generation = await context.ModerationEventGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.GenerationId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        generation.ValidatePersistedState();
        if (authority.EventGenerationPlanSourceId != generation.Id
            || authority.EventGenerationPlan is not { } plannedGeneration
            || plannedGeneration.Id != generation.EventId || plannedGeneration.ActorId != generation.ActorId
            || plannedGeneration.OrganizerActorId != generation.OrganizerActorId
            || binding is null || !authority.Tenant.Contains(binding.Scope)
            || binding.Scope.Kind is not (ModerationScopeKind.Tenant or ModerationScopeKind.Resource)
            || binding.Scope.Kind == ModerationScopeKind.Resource && binding.Scope.Id != generation.EventId
            || binding.SubjectKind != decision.Subject.Kind || binding.SubjectId != decision.Subject.Id
            || !generation.MatchesNativeSubject(decision.Subject.Kind, decision.Subject.Id)
            || generation.HostBindingId != authority.Binding.Id || generation.TrustDomainId != authority.Binding.TrustDomainId
            || generation.TenantId != authority.Tenant.Id || binding.TargetId != generation.EventId
            || binding.TargetRevision != generation.Revision || generation.Revision != source.ExpectedGenerationRevision
            || generation.ObservedAt > now || generation.ValidUntil <= now || source.ValidUntil > generation.ValidUntil
            || !authority.FencedHumans.Contains(generation.ObservedByHumanId)
            || !authority.Administrators.Contains(generation.ObservedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var native = await ModerationEventGenerationReader.AcquireAsync(context, generation.Id, generation.EventId,
            generation.HostBindingId, generation.TrustDomainId, generation.TenantId, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, generation.ValidUntil, token,
            generation, ownershipPlan: plannedGeneration);
        if (native is null || !generation.HasSameNativeVersion(native))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await using var generationSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.event-generation:{authority.Binding.Id:N}:{generation.EventId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventGeneration>(context, generation.Id, token);
        if (await context.ModerationEventGenerations.AsNoTracking().AnyAsync(
            row => row.HostBindingId == authority.Binding.Id && row.EventId == generation.EventId
                && row.Revision > generation.Revision, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        await RequireTargetSourceReceiptAsync(new RecordModerationEventGenerationSource
        {
            OperationId = Guid.Empty, Target = authority.Tenant, SourceId = generation.Id, EventId = generation.EventId,
            ExpectedEventRevision = generation.Revision - 1, ExpectedQualificationRevision = generation.WriterGeneration - 1,
            ValidUntil = generation.ValidUntil
        }, ModerationQualificationSourceKind.EventGeneration, generation.Id, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, authority, token);
        await using var selectionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.contribution:{authority.Binding.Id:N}:{selected.EffectId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, selected.Id, token);
        await RequireTargetSourceReceiptAsync(new RecordModerationContributionSelectionSource
        {
            OperationId = Guid.Empty, Target = decision.Scope, ExpectedQualificationRevision = selected.WriterGeneration - 1,
            SelectionId = selected.Id, EffectId = selected.EffectId, CaseId = selected.CaseId,
            ExpectedCaseRevision = selected.CaseRevision, DecisionId = decision.Id, ExpectedDecisionRevision = selected.DecisionRevision,
            PolicyId = selected.PolicyId, ExpectedPolicyRevision = selected.PolicyRevision, PublicationId = selected.PublicationId,
            ProfileId = selected.ProfileId, Contribution = contribution, ValidUntil = selected.ValidUntil
        }, ModerationQualificationSourceKind.ContributionSelection, selected.Id, selected.Revision,
            selected.AuthoredByHumanId, selected.WriterGeneration, selected.RecordedAt, authority, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.event-target:{selected.Id:N}:{binding.Ordinal}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventTargetAdoption>(context, source.AdoptionId, token);
        if (await context.ModerationEventTargetAdoptions.AsNoTracking().AnyAsync(
            row => row.Id == source.AdoptionId || row.SelectionId == selected.Id && row.BindingOrdinal == binding.Ordinal, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationEventTargetAdoption.Adopt(source.AdoptionId, selected, binding, generation,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, source.ValidUntil);
        context.ModerationEventTargetAdoptions.Add(adoption);
        return new(ModerationQualificationSourceKind.EventTargetAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> RevokeEventTargetAsync(
        RevokeModerationEventTargetSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token,
            requireOpen: false, requireCurrentAttribution: false);
        var discovered = await context.ModerationEventTargetAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.event-target:{discovered.SelectionId:N}:{discovered.BindingOrdinal}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventTargetAdoption>(context, source.AdoptionId, token);
        var adoption = await context.ModerationEventTargetAdoptions.SingleAsync(row => row.Id == source.AdoptionId, token);
        adoption.ValidatePersistedState();
        if (adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != authority.Binding.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || adoption.CaseId != source.CaseId
            || adoption.SelectionId != discovered.SelectionId || adoption.BindingOrdinal != discovered.BindingOrdinal
            || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (adoption.Revision != source.ExpectedAdoptionRevision || adoption.RevokedAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Revoke(source.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.EventTargetAdoption, adoption.Id, adoption.Revision);
    }
}
