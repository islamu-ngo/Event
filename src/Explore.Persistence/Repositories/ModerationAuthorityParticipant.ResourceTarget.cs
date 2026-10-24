using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// Qualifies every ordinal in a retained resource-only contribution before exposing
    /// the distinct target set. An unsupported or unadopted partition rejects the whole set.
    /// </summary>
    public async Task<ModerationPolicyPublicationNoticeApproval?> AcquireResourceNoticeApprovalAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationEffectIntent effect,
        ModerationDecision decision, ModerationAuthorityGrant grant, DateTimeOffset observedAt, CancellationToken token)
    {
        var draft = await AcquireNoticeContributionDraftAsync(
            transactionId, publication, effect, decision, grant, observedAt, token);
        var acquired = _acquisition;
        if (draft is null || acquired is null
            || decision.Subject.Kind is not (ModerationSubjectKind.Human or ModerationSubjectKind.Content))
            return null;
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == draft.SelectionId, token);
        if (selection is null || selection.Revision != draft.SelectionRevision
            || acquired.Source.NativeTargets is not { } plan || !await plan.MatchesAsync(context, selection, token)) return null;
        selection.ValidatePersistedState();
        var adoptions = await context.ModerationResourceTargetAdoptions.AsNoTracking()
            .Where(row => row.SelectionId == selection.Id).ToListAsync(token);
        if (adoptions.Count != selection.Bindings.Count
            || selection.Bindings.Any(binding => !adoptions.Any(row => row.BindingOrdinal == binding.Ordinal)))
            return null;
        var generationIds = adoptions.Select(row => row.GenerationId).Distinct().ToArray();
        var generations = await context.ModerationResourceGenerations.AsNoTracking()
            .Where(row => generationIds.Contains(row.Id)).ToListAsync(token);
        if (generations.Count != generationIds.Length
            || generations.Any(row => row.HostBindingId != acquired.Source.Binding.Id
                || row.TrustDomainId != effect.TrustDomainId || row.TenantId != acquired.TenantId))
            return null;
        foreach (var generation in generations) generation.ValidatePersistedState();

        // All Actor anchors came from the acquired complete plan before Tenant/Epoch.
        foreach (var id in generations.Select(row => row.EventId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Event>(context, id, token);
        foreach (var id in generations.Select(row => row.ResourceId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<EventResource>(context, id, token);
        foreach (var id in generations.Select(row => row.StorageObjectId).OfType<Guid>().Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<StorageObject>(context, id, token);
        foreach (var id in generations.Select(row => row.StorageProviderBindingId).OfType<Guid>().Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<StorageProviderBinding>(context, id, token);

        var targets = ImmutableArray.CreateBuilder<ModerationNoticeTargetObservation>();
        var until = draft.ValidUntil;
        foreach (var binding in selection.Bindings.OrderBy(row => row.Ordinal))
        {
            var observed = await AcquireResourceTargetObservationAsync(
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
    /// Independently rereads one accepted resource adoption and current native generation.
    /// The complete notice owner must acquire every selected partition, not just this one.
    /// </summary>
    public async Task<ModerationNoticeTargetObservation?> AcquireResourceTargetObservationAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationEffectIntent effect,
        ModerationDecision decision, ModerationAuthorityGrant grant, int bindingOrdinal,
        DateTimeOffset observedAt, CancellationToken token)
    {
        var draft = await AcquireNoticeContributionDraftAsync(
            transactionId, publication, effect, decision, grant, observedAt, token);
        var acquired = _acquisition;
        if (draft is null || acquired is null || bindingOrdinal is < 0 or >= 64
            || decision.Subject.Kind is not (ModerationSubjectKind.Human or ModerationSubjectKind.Content))
            return null;
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == draft.SelectionId, token);
        if (selection is null || selection.Revision != draft.SelectionRevision
            || acquired.Source.NativeTargets is not { } plan || !await plan.MatchesAsync(context, selection, token)) return null;
        selection.ValidatePersistedState();
        return await AcquireResourceTargetObservationAsync(
            draft, selection, acquired, effect, decision, grant, bindingOrdinal, observedAt, token);
    }

    private async Task<ModerationNoticeTargetObservation?> AcquireResourceTargetObservationAsync(
        ModerationNoticeNativeContributionDraft draft, ModerationContributionSelection selection,
        Acquisition acquired, ModerationEffectIntent effect, ModerationDecision decision,
        ModerationAuthorityGrant grant, int bindingOrdinal, DateTimeOffset observedAt, CancellationToken token)
    {
        var binding = selection.Bindings.SingleOrDefault(row => row.Ordinal == bindingOrdinal);
        if (binding is null || !acquired.Source.Tenant.Contains(binding.Scope)
            || binding.Scope.Kind is not (ModerationScopeKind.Tenant or ModerationScopeKind.Resource)
            || decision.Subject.Kind == ModerationSubjectKind.Content
                && (decision.Subject.Origin != acquired.Source.Tenant || decision.Scope.Kind != ModerationScopeKind.Resource
                    || decision.Scope.Parent != acquired.Source.Tenant || binding.Scope != decision.Scope)
            || binding.SubjectKind != decision.Subject.Kind || binding.SubjectId != decision.Subject.Id)
            return null;
        var discovered = await context.ModerationResourceTargetAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SelectionId == selection.Id && row.BindingOrdinal == bindingOrdinal, token);
        if (discovered is null || !acquired.Source.FencedHumans.Contains(discovered.AdoptedByHumanId)
            || !acquired.Source.Administrators.Contains(discovered.AdoptedByHumanId))
            return null;
        var generation = await context.ModerationResourceGenerations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.GenerationId, token);
        if (generation is null || acquired.Source.NativeTargets?.Resource(selection.Id, discovered, generation) is not { } ancestryPlan)
            return null;
        generation.ValidatePersistedState();
        if (generation.HostBindingId != acquired.Source.Binding.Id || generation.TrustDomainId != effect.TrustDomainId
            || generation.TenantId != acquired.TenantId || generation.ResourceId != binding.TargetId
            || generation.Revision != binding.TargetRevision || generation.ObservedAt > observedAt
            || generation.ValidUntil <= observedAt || generation.WriterGeneration > grant.QualificationRevision
            || binding.Scope.Kind == ModerationScopeKind.Resource && binding.Scope.Id != generation.ResourceId
            || !acquired.Source.FencedHumans.Contains(generation.ObservedByHumanId)
            || !acquired.Source.Administrators.Contains(generation.ObservedByHumanId)
            || !await context.Users.AsNoTracking().AnyAsync(
                row => row.Id == generation.ObservedByHumanId && !row.IsDeleted, token))
            return null;
        // Expected lineage rejects reparenting before introducing replacement Actor/storage
        // fences. The current source comes from a real native reread, never an approval clone.
        var native = await ModerationResourceGenerationReader.AcquireAsync(context, generation.Id, generation.ResourceId,
            generation.HostBindingId, generation.TrustDomainId, generation.TenantId, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt,
            generation.ValidUntil, token, generation, ancestryPlan: ancestryPlan);
        if (native is null || !generation.HasSameNativeVersion(native)
            || (decision.Subject.Kind == ModerationSubjectKind.Content
                ? generation.EventId != decision.Subject.Id
                : !await context.Events.AsNoTracking().AnyAsync(row => row.Id == generation.EventId
                    && row.SubmittedByUserId == decision.Subject.Id, token)))
            return null;
        await using var generationSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.resource-generation:{acquired.Source.Binding.Id:N}:{generation.ResourceId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceGeneration>(context, generation.Id, token);
        if (await context.ModerationResourceGenerations.AsNoTracking().AnyAsync(
            row => row.HostBindingId == acquired.Source.Binding.Id && row.ResourceId == generation.ResourceId
                && row.Revision > generation.Revision, token))
            return null;
        if (!await HasNativeTargetReceiptAsync(operationId => new RecordModerationResourceGenerationSource
        {
            OperationId = operationId,
            Target = acquired.Source.Tenant,
            SourceId = generation.Id,
            ResourceId = generation.ResourceId,
            ExpectedResourceRevision = generation.Revision - 1,
            ExpectedQualificationRevision = generation.WriterGeneration - 1,
            ValidUntil = generation.ValidUntil
        }, ModerationQualificationSourceKind.ResourceGeneration, generation.Id, generation.Revision,
            generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, acquired, token))
            return null;
        await using var adoptionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.resource-target:{selection.Id:N}:{bindingOrdinal}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceTargetAdoption>(context, discovered.Id, token);
        var adoption = await context.ModerationResourceTargetAdoptions.AsNoTracking()
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
        if (!await HasNativeTargetReceiptAsync(operationId => new AdoptModerationResourceTargetSource
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
        }, ModerationQualificationSourceKind.ResourceTargetAdoption, adoption.Id, 1,
            adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, acquired, token))
            return null;
        var until = draft.ValidUntil < adoption.ValidUntil ? draft.ValidUntil : adoption.ValidUntil;
        if (!await CurrentAsync(acquired, token)) return null;
        var completedAt = clock.GetUtcNow();
        return completedAt >= observedAt && completedAt < until
            ? new(new ModerationPolicyTarget(binding.Scope, binding.SubjectKind, binding.SubjectId,
                    binding.TargetId, binding.TargetRevision, binding.Operation, binding.WindowEnd),
                "event.resource-generation",
                new(adoption.GenerationId, adoption.GenerationRevision,
                    adoption.GenerationWriterGeneration.ToString(CultureInfo.InvariantCulture)),
                new(native.Id, native.Revision, native.WriterGeneration.ToString(CultureInfo.InvariantCulture)),
                observedAt, until)
            : null;
    }

    private async Task<bool> HasNativeTargetReceiptAsync(
        Func<Guid, ModerationQualificationMutation> request, ModerationQualificationSourceKind kind, Guid id, long revision,
        Guid principalId, long writerGeneration, DateTimeOffset completedAt, Acquisition acquired, CancellationToken token)
    {
        var receiptId = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceId == id && row.SourceKind == kind && row.ResultRevision == revision)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (receiptId is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId.Value, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId.Value, token);
        if (receipt is null) return false;
        receipt.ValidatePersistedState();
        var accepted = request(receipt.Id);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            accepted.GetType().FullName + "\n" + JsonSerializer.Serialize(accepted, accepted.GetType()))));
        return receipt.SourceId == id && receipt.SourceKind == kind && receipt.ResultRevision == revision
            && receipt.RequestDigest == digest && receipt.HostBindingId == acquired.Source.Binding.Id
            && receipt.TrustDomainId == acquired.Source.Binding.TrustDomainId && receipt.TenantId == acquired.TenantId
            && receipt.PrincipalId == principalId && receipt.QualificationRevision == writerGeneration
            && receipt.CompletedAt == completedAt && writerGeneration <= acquired.Grant.QualificationRevision;
    }
}
