using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task<Guid?> ProfileCollectionProcedurePublicationAsync(
        ModerationQualificationMutation? mutation, CancellationToken token)
    {
        if (mutation is RecordModerationProfileCollectionProcedureSource record)
            return await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
                .Where(row => row.Id == record.ChildAgeSourceId).Select(row => (Guid?)row.PublicationId).SingleOrDefaultAsync(token);
        Guid? sourceId = mutation is AdoptModerationProfileCollectionProcedureSource adopt ? adopt.SourceId : null;
        if (mutation is WithdrawModerationProfileCollectionProcedureAdoption withdraw)
            sourceId = await context.Set<ModerationProfileCollectionProcedureAdoption>().AsNoTracking()
                .Where(row => row.Id == withdraw.AdoptionId).Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        return sourceId is null ? null : await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
            .Where(row => row.Id == sourceId).Select(row => (Guid?)row.PublicationId).SingleOrDefaultAsync(token);
    }

    private async Task DiscoverProfileCollectionProcedureHumansAsync(
        ModerationQualificationMutation? mutation, ModerationOperation? operation, Guid? publicationCandidateId,
        Guid? candidateId, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is not (RecordModerationProfileCollectionProcedureSource or AdoptModerationProfileCollectionProcedureSource
            or WithdrawModerationProfileCollectionProcedureAdoption) && operation is null && publicationCandidateId is null) return;
        var publications = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token);
        if (candidateId is { } candidate) publications.Add(candidate);
        var sources = await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && publications.Contains(row.PublicationId)).ToListAsync(token);
        var parentIds = sources.Select(row => row.ChildAgeSourceId).ToList();
        if (mutation is RecordModerationProfileCollectionProcedureSource record) parentIds.Add(record.ChildAgeSourceId);
        foreach (var source in sources)
        {
            humans.Add(source.RecordedByHumanId);
            AddOwners(source.ChildAgeOwnership);
            humans.Add(source.ChildAgeRecordedByHumanId);
        }
        var parents = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
            .Where(row => parentIds.Contains(row.Id) && row.HostBindingId == hostId && row.TenantId == tenantId).ToListAsync(token);
        foreach (var parent in parents)
        {
            humans.Add(parent.RecordedByHumanId);
            AddOwners(parent.Ownership);
        }
        var ids = sources.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationProfileCollectionProcedureAdoption>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId)).ToListAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
        }
        foreach (var adoption in await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking()
            .Where(row => parentIds.Contains(row.SourceId)).ToListAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
        }
        void AddOwners(ModerationChildAgePolicyOwnership ownership)
        {
            humans.Add(ownership.CollectionOwnerHumanId);
            humans.Add(ownership.CollectionControllerHumanId);
            humans.Add(ownership.ReviewerHumanId);
            humans.Add(ownership.ReviewerControllerHumanId);
            humans.Add(ownership.GovernanceControllerHumanId);
        }
    }
}
