using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private async Task<QualifiedSource> AdoptResourceTargetAsync(
        AdoptModerationResourceTargetSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
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
            || decision.Subject.Kind is not (ModerationSubjectKind.Human or ModerationSubjectKind.Content)
            || decision.Subject.Kind == ModerationSubjectKind.Content
                && (decision.Subject.Origin != authority.Tenant || decision.Scope.Kind != ModerationScopeKind.Resource
                    || decision.Scope.Parent != authority.Tenant)
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
        var generation = await context.ModerationResourceGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.GenerationId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        generation.ValidatePersistedState();
        var plannedGeneration = authority.ResourceGenerationPlan;
        if (authority.ResourceGenerationPlanSourceId != generation.Id || plannedGeneration is null
            || plannedGeneration.ResourceId != generation.ResourceId || plannedGeneration.EventId != generation.EventId
            || plannedGeneration.ActorId != generation.ActorId || plannedGeneration.OrganizerActorId != generation.OrganizerActorId
            || plannedGeneration.StorageObjectId != generation.StorageObjectId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (binding is null || !authority.Tenant.Contains(binding.Scope)
            || binding.Scope.Kind is not (ModerationScopeKind.Tenant or ModerationScopeKind.Resource)
            || binding.Scope.Kind == ModerationScopeKind.Resource && binding.Scope.Id != generation.ResourceId
            || decision.Subject.Kind == ModerationSubjectKind.Content && binding.Scope != decision.Scope
            || binding.SubjectKind != decision.Subject.Kind || binding.SubjectId != decision.Subject.Id
            || generation.HostBindingId != authority.Binding.Id || generation.TrustDomainId != authority.Binding.TrustDomainId
            || generation.TenantId != authority.Tenant.Id || binding.TargetId != generation.ResourceId
            || binding.TargetRevision != generation.Revision || generation.Revision != source.ExpectedGenerationRevision
            || generation.ObservedAt > now || generation.ValidUntil <= now || source.ValidUntil > generation.ValidUntil
            || !authority.FencedHumans.Contains(generation.ObservedByHumanId)
            || !authority.Administrators.Contains(generation.ObservedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var native = await ModerationResourceGenerationReader.AcquireAsync(context, generation.Id, generation.ResourceId,
            generation.HostBindingId, generation.TrustDomainId, generation.TenantId, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, generation.ValidUntil, token, generation,
            ancestryPlan: plannedGeneration);
        if (native is null || !generation.HasSameNativeVersion(native)
            || (decision.Subject.Kind == ModerationSubjectKind.Content
                ? generation.EventId != decision.Subject.Id
                : !await context.Events.AsNoTracking().AnyAsync(row => row.Id == generation.EventId
                    && row.SubmittedByUserId == decision.Subject.Id, token)))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        // Native resource lineage is held before generation and retained-selection slots.
        await using var generationSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.resource-generation:{authority.Binding.Id:N}:{generation.ResourceId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceGeneration>(context, generation.Id, token);
        if (await context.ModerationResourceGenerations.AsNoTracking().AnyAsync(
            row => row.HostBindingId == authority.Binding.Id && row.ResourceId == generation.ResourceId
                && row.Revision > generation.Revision, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        await RequireTargetSourceReceiptAsync(new RecordModerationResourceGenerationSource
        {
            OperationId = Guid.Empty,
            Target = authority.Tenant,
            SourceId = generation.Id,
            ResourceId = generation.ResourceId,
            ExpectedResourceRevision = generation.Revision - 1,
            ExpectedQualificationRevision = generation.WriterGeneration - 1,
            ValidUntil = generation.ValidUntil
        }, ModerationQualificationSourceKind.ResourceGeneration, generation.Id, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, authority, token);
        await using var selectionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.contribution:{authority.Binding.Id:N}:{selected.EffectId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, selected.Id, token);
        await RequireTargetSourceReceiptAsync(new RecordModerationContributionSelectionSource
        {
            OperationId = Guid.Empty,
            Target = decision.Scope,
            ExpectedQualificationRevision = selected.WriterGeneration - 1,
            SelectionId = selected.Id,
            EffectId = selected.EffectId,
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
            selected.AuthoredByHumanId, selected.WriterGeneration, selected.RecordedAt, authority, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.resource-target:{selected.Id:N}:{binding.Ordinal}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceTargetAdoption>(context, source.AdoptionId, token);
        if (await context.ModerationResourceTargetAdoptions.AsNoTracking().AnyAsync(
            row => row.Id == source.AdoptionId || row.SelectionId == selected.Id && row.BindingOrdinal == binding.Ordinal, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationResourceTargetAdoption.Adopt(source.AdoptionId, selected, binding, generation,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, source.ValidUntil);
        context.ModerationResourceTargetAdoptions.Add(adoption);
        return new(ModerationQualificationSourceKind.ResourceTargetAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> RevokeResourceTargetAsync(
        RevokeModerationResourceTargetSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token,
            requireOpen: false, requireCurrentAttribution: false);
        var discovered = await context.ModerationResourceTargetAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.resource-target:{discovered.SelectionId:N}:{discovered.BindingOrdinal}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceTargetAdoption>(context, source.AdoptionId, token);
        var adoption = await context.ModerationResourceTargetAdoptions.SingleAsync(row => row.Id == source.AdoptionId, token);
        adoption.ValidatePersistedState();
        if (adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != authority.Binding.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || adoption.CaseId != source.CaseId
            || adoption.SelectionId != discovered.SelectionId || adoption.BindingOrdinal != discovered.BindingOrdinal
            || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (adoption.Revision != source.ExpectedAdoptionRevision || adoption.RevokedAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Revoke(source.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.ResourceTargetAdoption, adoption.Id, adoption.Revision);
    }

    private async Task RequireTargetSourceReceiptAsync(
        ModerationQualificationMutation template, ModerationQualificationSourceKind kind, Guid id, long revision,
        Guid principalId, long generation, DateTimeOffset recordedAt, ModerationSourceAuthority authority, CancellationToken token)
    {
        var receiptId = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceKind == kind && row.SourceId == id && row.ResultRevision == revision)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleAsync(row => row.Id == receiptId, token);
        receipt.ValidatePersistedState();
        var request = template with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        if (receipt.RequestDigest != digest || receipt.SourceKind != kind || receipt.SourceId != id || receipt.ResultRevision != revision
            || receipt.HostBindingId != authority.Binding.Id
            || receipt.TrustDomainId != authority.Binding.TrustDomainId || receipt.TenantId != authority.Tenant.Id
            || receipt.PrincipalId != principalId || receipt.QualificationRevision != generation
            || receipt.CompletedAt != recordedAt || generation > authority.Epoch.Revision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }
}
