using System.Collections.Immutable;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAuthorityParticipant : IModerationPolicyPublicationNoticeAuthority
{
    Task<ModerationPolicyPublicationNoticeApproval?> IModerationPolicyPublicationNoticeAuthority.AcquireAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationEffectIntent effect,
        ModerationDecision decision, ModerationSanction? sanction, ModerationAuthorityGrant grant,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        if (effect.SanctionId != sanction?.Id || effect.SanctionRevision != sanction?.Revision)
            return Task.FromResult<ModerationPolicyPublicationNoticeApproval?>(null);
        return AcquireNativeNoticeApprovalAsync(
            transactionId, publication, effect, decision, grant, observedAt, cancellationToken);
    }

    /// <summary>
    /// Joins independently qualified Event/resource owners for every retained ordinal.
    /// Missing, unsupported or ambiguous owner coverage rejects the entire selection.
    /// </summary>
    public async Task<ModerationPolicyPublicationNoticeApproval?> AcquireNativeNoticeApprovalAsync(
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
        var resources = await context.ModerationResourceTargetAdoptions.AsNoTracking()
            .Where(row => row.SelectionId == selection.Id).ToListAsync(token);
        var events = await context.ModerationEventTargetAdoptions.AsNoTracking()
            .Where(row => row.SelectionId == selection.Id).ToListAsync(token);
        if (decision.Subject.Kind == ModerationSubjectKind.Content && events.Count != 0
            || resources.Count + events.Count != selection.Bindings.Count
            || selection.Bindings.Any(binding => resources.Count(row => row.BindingOrdinal == binding.Ordinal)
                + events.Count(row => row.BindingOrdinal == binding.Ordinal) != 1))
            return null;
        var resourceGenerationIds = resources.Select(row => row.GenerationId).Distinct().ToArray();
        var eventGenerationIds = events.Select(row => row.GenerationId).Distinct().ToArray();
        var resourceGenerations = await context.ModerationResourceGenerations.AsNoTracking()
            .Where(row => resourceGenerationIds.Contains(row.Id)).ToListAsync(token);
        var eventGenerations = await context.ModerationEventGenerations.AsNoTracking()
            .Where(row => eventGenerationIds.Contains(row.Id)).ToListAsync(token);
        if (resourceGenerations.Count != resourceGenerationIds.Length || eventGenerations.Count != eventGenerationIds.Length
            || resourceGenerations.Any(row => row.HostBindingId != acquired.Source.Binding.Id
                || row.TrustDomainId != effect.TrustDomainId || row.TenantId != acquired.TenantId)
            || eventGenerations.Any(row => row.HostBindingId != acquired.Source.Binding.Id
                || row.TrustDomainId != effect.TrustDomainId || row.TenantId != acquired.TenantId))
            return null;
        foreach (var generation in resourceGenerations) generation.ValidatePersistedState();
        foreach (var generation in eventGenerations) generation.ValidatePersistedState();

        // The complete Actor union is already held before Tenant/Epoch; never append parents here.
        foreach (var id in resourceGenerations.Select(row => row.EventId)
            .Concat(eventGenerations.Select(row => row.EventId)).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Event>(context, id, token);
        foreach (var id in resourceGenerations.Select(row => row.ResourceId).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<EventResource>(context, id, token);
        foreach (var id in resourceGenerations.Select(row => row.StorageObjectId).OfType<Guid>().Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<StorageObject>(context, id, token);
        foreach (var id in resourceGenerations.Select(row => row.StorageProviderBindingId).OfType<Guid>().Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<StorageProviderBinding>(context, id, token);

        var targets = ImmutableArray.CreateBuilder<ModerationNoticeTargetObservation>();
        var until = draft.ValidUntil;
        foreach (var binding in selection.Bindings.OrderBy(row => row.Ordinal))
        {
            var observed = resources.Any(row => row.BindingOrdinal == binding.Ordinal)
                ? await AcquireResourceTargetObservationAsync(
                    draft, selection, acquired, effect, decision, grant, binding.Ordinal, observedAt, token)
                : await AcquireEventTargetObservationAsync(
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
}
