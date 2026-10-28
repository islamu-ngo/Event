using Explore.Domain;
using Explore.Persistence.Extensions;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    private async Task<Relationship?> PublicEventRelationshipAsync(
        Guid id, ModerationCaseTargetSource source, NativeTarget native, bool acquire, CancellationToken token)
    {
        if (source.Kind != ModerationCaseTargetKind.EventVersion || source.NativeKind != ModerationCaseTargetKind.EventVersion
            || id != source.EventId || native.EventVersion is not { } eventVersion)
            return null;
        var parent = await context.Events.AsNoTracking().Where(row => row.Id == id)
            .Select(row => new { row.ActorId, row.AtprotoRecordId }).SingleOrDefaultAsync(token);
        if (parent is null || !await FenceAsync<Actor>(parent.ActorId, acquire, token)) return null;
        var actor = await context.Actors.AsNoTracking().Where(row => row.Id == parent.ActorId)
            .Select(row => new { row.UserId, row.OrganizationId, row.GroupId }).SingleOrDefaultAsync(token);
        if (actor is null) return null;
        if (!await FencePublicRowsAsync(context.TenantUsers.Where(row =>
                row.ActorId == parent.ActorId && row.UserId == actor.UserId), acquire, token)
            || !await FencePublicRowsAsync(context.OrganizationTenants.Where(row =>
                row.OrganizationId == actor.OrganizationId), acquire, token)
            || !await FencePublicRowsAsync(context.GroupTenants.Where(row =>
                row.GroupId == actor.GroupId), acquire, token))
            return null;
        if (parent.AtprotoRecordId is { } recordId)
        {
            if (!await FenceAsync<AtprotoRecord>(recordId, acquire, token)
                || !await FencePublicRowsAsync(context.AtprotoOutboundRecordOwnerships.Where(row =>
                    row.AtprotoRecordId == recordId && row.SourceEntityType == "Event" && row.SourceEntityId == id), acquire, token)
                || !await FencePublicRowsAsync(context.AtprotoRecordTenantPresentations.Where(row =>
                    row.AtprotoRecordId == recordId), acquire, token)
                || !await FencePublicRowsAsync(context.AtprotoIdentities.Where(row =>
                    row.ActorId == parent.ActorId), acquire, token))
                return null;
        }

        return await context.Events.AsNoTracking().WherePubliclyEligible(context)
            .AnyAsync(row => row.Id == id && row.ConcurrencyStamp == native.EventVersion, token)
            ? new(ModerationCaseRelationshipGeneration.FromStamp(eventVersion),
                DateTimeOffset.MaxValue, $"public-event:{id:D}:{native.EventVersion:D}") : null;
    }

    private async Task<bool> FencePublicRowsAsync<TEntity>(
        IQueryable<TEntity> rows, bool acquire, CancellationToken token) where TEntity : class
    {
        var ids = await rows.Select(row => EF.Property<Guid>(row, "Id")).OrderBy(id => id).ToArrayAsync(token);
        foreach (var id in ids)
            if (!await FenceAsync<TEntity>(id, acquire, token)) return false;
        return true;
    }
}
