using System.Collections.Immutable;
using System.Globalization;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAuthorityParticipant
{
    /// <summary>
    /// Qualifies every ordinal of a retained Event-only contribution before exposing
    /// its distinct native targets. Unsupported or unadopted partitions reject the set.
    /// </summary>
    public async Task<ModerationPolicyPublicationNoticeApproval?> AcquireEventNoticeApprovalAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationEffectIntent effect,
        ModerationDecision decision, ModerationAuthorityGrant grant, DateTimeOffset observedAt, CancellationToken token)
    {
        var draft = await AcquireNoticeContributionDraftAsync(
            transactionId, publication, effect, decision, grant, observedAt, token);
        var acquired = _acquisition;
        if (draft is null || acquired is null || decision.Subject.Kind != ModerationSubjectKind.Human) return null;
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == draft.SelectionId, token);
        if (selection is null || selection.Revision != draft.SelectionRevision
            || acquired.Source.NativeTargets is not { } plan || !await plan.MatchesAsync(context, selection, token)) return null;
        selection.ValidatePersistedState();
        var adoptions = await context.ModerationEventTargetAdoptions.AsNoTracking()
            .Where(row => row.SelectionId == selection.Id).ToListAsync(token);
        if (adoptions.Count != selection.Bindings.Count
            || selection.Bindings.Any(binding => !adoptions.Any(row => row.BindingOrdinal == binding.Ordinal)))
            return null;
        var generationIds = adoptions.Select(row => row.GenerationId).Distinct().ToArray();
        var generations = await context.ModerationEventGenerations.AsNoTracking()
            .Where(row => generationIds.Contains(row.Id)).ToListAsync(token);
        if (generations.Count != generationIds.Length
            || generations.Any(row => row.HostBindingId != acquired.Source.Binding.Id
                || row.TrustDomainId != effect.TrustDomainId || row.TenantId != acquired.TenantId))
            return null;
        foreach (var generation in generations) generation.ValidatePersistedState();

        // All Actor anchors came from the acquired complete plan before Tenant/Epoch.
        foreach (var id in generations.Select(row => row.EventId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Event>(context, id, token);

        var targets = ImmutableArray.CreateBuilder<ModerationNoticeTargetObservation>();
        var until = draft.ValidUntil;
        foreach (var binding in selection.Bindings.OrderBy(row => row.Ordinal))
        {
            var observed = await AcquireEventTargetObservationAsync(
                draft, selection, acquired, effect, decision, grant, binding.Ordinal, observedAt, token);
            if (observed is null || observed.ApprovedSource != observed.CurrentSource) return null;
            if (observed.ValidUntil < until) until = observed.ValidUntil;
            var duplicate = targets.FirstOrDefault(row => row.Target == observed.Target);
            if (duplicate is not null)
            {
                if (duplicate.Owner != observed.Owner || duplicate.ApprovedSource != observed.ApprovedSource
                    || duplicate.CurrentSource != observed.CurrentSource)
                    return null;
            }
            else targets.Add(observed);
        }
        if (!await CurrentAsync(acquired, token)) return null;
        var completedAt = clock.GetUtcNow();
        if (completedAt < observedAt || completedAt >= until) return null;
        return new(transactionId, observedAt, until, draft.Notice.Content, draft.Contribution,
            targets.Select(row => row with { ValidUntil = until }).ToImmutableArray(), draft.Notice.Reviewers);
    }

    /// <summary>
    /// Independently qualifies one accepted Event adoption against a genuine native reread.
    /// Complete notice ownership still requires every retained contribution ordinal.
    /// </summary>
    public async Task<ModerationNoticeTargetObservation?> AcquireEventTargetObservationAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationEffectIntent effect,
        ModerationDecision decision, ModerationAuthorityGrant grant, int bindingOrdinal,
        DateTimeOffset observedAt, CancellationToken token)
    {
        var draft = await AcquireNoticeContributionDraftAsync(
            transactionId, publication, effect, decision, grant, observedAt, token);
        var acquired = _acquisition;
        if (draft is null || acquired is null || bindingOrdinal is < 0 or >= 64
            || decision.Subject.Kind != ModerationSubjectKind.Human)
            return null;
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == draft.SelectionId, token);
        if (selection is null || selection.Revision != draft.SelectionRevision
            || acquired.Source.NativeTargets is not { } plan || !await plan.MatchesAsync(context, selection, token)) return null;
        selection.ValidatePersistedState();
        return await AcquireEventTargetObservationAsync(
            draft, selection, acquired, effect, decision, grant, bindingOrdinal, observedAt, token);
    }

    private async Task<ModerationNoticeTargetObservation?> AcquireEventTargetObservationAsync(
        ModerationNoticeNativeContributionDraft draft, ModerationContributionSelection selection,
        Acquisition acquired, ModerationEffectIntent effect, ModerationDecision decision,
        ModerationAuthorityGrant grant, int bindingOrdinal, DateTimeOffset observedAt, CancellationToken token)
    {
        var binding = selection.Bindings.SingleOrDefault(row => row.Ordinal == bindingOrdinal);
        if (binding is null || !acquired.Source.Tenant.Contains(binding.Scope)
            || binding.Scope.Kind is not (ModerationScopeKind.Tenant or ModerationScopeKind.Resource)
            || binding.SubjectKind != decision.Subject.Kind || binding.SubjectId != decision.Subject.Id)
            return null;
        var discovered = await context.ModerationEventTargetAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SelectionId == selection.Id && row.BindingOrdinal == bindingOrdinal, token);
        if (discovered is null || !acquired.Source.FencedHumans.Contains(discovered.AdoptedByHumanId)
            || !acquired.Source.Administrators.Contains(discovered.AdoptedByHumanId))
            return null;
        var generation = await context.ModerationEventGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.GenerationId, token);
        if (generation is null || acquired.Source.NativeTargets?.Event(selection.Id, discovered, generation) is not { } ownershipPlan)
            return null;
        generation.ValidatePersistedState();
        if (generation.HostBindingId != acquired.Source.Binding.Id || generation.TrustDomainId != effect.TrustDomainId
            || generation.TenantId != acquired.TenantId || generation.EventId != binding.TargetId
            || generation.Revision != binding.TargetRevision || generation.ObservedAt > observedAt
            || generation.ValidUntil <= observedAt || generation.WriterGeneration > grant.QualificationRevision
            || !generation.MatchesNativeSubject(decision.Subject.Kind, decision.Subject.Id)
            || binding.Scope.Kind == ModerationScopeKind.Resource && binding.Scope.Id != generation.EventId
            || !acquired.Source.FencedHumans.Contains(generation.ObservedByHumanId)
            || !acquired.Source.Administrators.Contains(generation.ObservedByHumanId)
            || !await context.Users.AsNoTracking().AnyAsync(
                row => row.Id == generation.ObservedByHumanId && !row.IsDeleted, token))
            return null;
        var native = await ModerationEventGenerationReader.AcquireAsync(context, generation.Id, generation.EventId,
            generation.HostBindingId, generation.TrustDomainId, generation.TenantId, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, generation.ValidUntil, token, generation,
            ownershipPlan: ownershipPlan);
        if (native is null || !generation.HasSameNativeVersion(native)) return null;
        await using var generationSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.event-generation:{acquired.Source.Binding.Id:N}:{generation.EventId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventGeneration>(context, generation.Id, token);
        if (await context.ModerationEventGenerations.AsNoTracking().AnyAsync(
            row => row.HostBindingId == acquired.Source.Binding.Id && row.EventId == generation.EventId
                && row.Revision > generation.Revision, token))
            return null;
        if (!await HasNativeTargetReceiptAsync(operationId => new RecordModerationEventGenerationSource
        {
            OperationId = operationId,
            Target = acquired.Source.Tenant,
            SourceId = generation.Id,
            EventId = generation.EventId,
            ExpectedEventRevision = generation.Revision - 1,
            ExpectedQualificationRevision = generation.WriterGeneration - 1,
            ValidUntil = generation.ValidUntil
        }, ModerationQualificationSourceKind.EventGeneration, generation.Id, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, acquired, token))
            return null;
        await using var adoptionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.event-target:{selection.Id:N}:{bindingOrdinal}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventTargetAdoption>(context, discovered.Id, token);
        var adoption = await context.ModerationEventTargetAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (adoption is null) return null;
        adoption.ValidatePersistedState();
        if (adoption.HostBindingId != acquired.Source.Binding.Id || adoption.TrustDomainId != effect.TrustDomainId
            || adoption.TenantId != acquired.TenantId || adoption.SelectionId != selection.Id
            || adoption.SelectionRevision != selection.Revision || adoption.BindingOrdinal != bindingOrdinal
            || adoption.GenerationId != generation.Id || adoption.GenerationRevision != generation.Revision
            || adoption.GenerationWriterGeneration != generation.WriterGeneration
            || adoption.CaseId != decision.CaseId || adoption.CaseRevision != acquired.Case!.Revision
            || adoption.AdoptedByHumanId != discovered.AdoptedByHumanId || adoption.AdoptedByHumanId == selection.AuthoredByHumanId
            || adoption.AdoptedByHumanId == decision.IssuerId || adoption.AdoptedByHumanId == decision.Subject.Id
            || adoption.RevokedAt is not null || adoption.AdoptedAt > observedAt || adoption.ValidUntil <= observedAt
            || adoption.ValidUntil > generation.ValidUntil || adoption.ValidUntil > selection.ValidUntil
            || adoption.WriterGeneration > grant.QualificationRevision
            || !await context.Users.AsNoTracking().AnyAsync(
                row => row.Id == adoption.AdoptedByHumanId && !row.IsDeleted, token))
            return null;
        if (!await HasNativeTargetReceiptAsync(operationId => new AdoptModerationEventTargetSource
        {
            OperationId = operationId,
            Target = decision.Scope,
            ExpectedQualificationRevision = adoption.WriterGeneration - 1,
            AdoptionId = adoption.Id,
            SelectionId = selection.Id,
            BindingOrdinal = bindingOrdinal,
            GenerationId = generation.Id,
            ExpectedGenerationRevision = adoption.GenerationRevision,
            CaseId = adoption.CaseId,
            ExpectedCaseRevision = adoption.CaseRevision,
            ValidUntil = adoption.ValidUntil
        }, ModerationQualificationSourceKind.EventTargetAdoption, adoption.Id, 1,
            adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, acquired, token))
            return null;
        var until = draft.ValidUntil < adoption.ValidUntil ? draft.ValidUntil : adoption.ValidUntil;
        if (!await CurrentAsync(acquired, token)) return null;
        var completedAt = clock.GetUtcNow();
        return completedAt >= observedAt && completedAt < until
            ? new(new ModerationPolicyTarget(binding.Scope, binding.SubjectKind, binding.SubjectId,
                    binding.TargetId, binding.TargetRevision, binding.Operation, binding.WindowEnd),
                "event.event-generation",
                new(adoption.GenerationId, adoption.GenerationRevision,
                    adoption.GenerationWriterGeneration.ToString(CultureInfo.InvariantCulture)),
                new(native.Id, native.Revision, native.WriterGeneration.ToString(CultureInfo.InvariantCulture)),
                observedAt, until)
            : null;
    }
}
