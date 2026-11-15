using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings.Groups;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Projects persisted support facts for the originally captured subject and session selector.</summary>
internal static class HumanSupportAccessSourceProjection
{
    internal static async Task<HumanSupportAccessSourceFacts> ReadAsync(
        ExploreDbContext context, Guid? tenant, HumanSupportForwardingObservation forwarding,
        Action requireCurrent, CancellationToken cancellationToken)
    {
        requireCurrent();
        var sessions = await context.SupportAccessSessions.AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .Where(row => row.Id == forwarding.SessionId && row.ActorUserId == forwarding.Subject.PrincipalId
                && (forwarding.ResolvedTenantId == null || row.TargetTenantId == forwarding.ResolvedTenantId)
                && (tenant == null || row.TargetTenantId == tenant))
            .Select(row => new HumanSupportSessionFact(row.Id, row.ActorUserId, row.TargetTenantId,
                row.TargetTenantUserId, row.StatusId, row.ModeId, row.StartedAtUtc, row.ExpiresAtUtc,
                row.EndedAtUtc, row.ReasonCode, row.TicketReference, row.ConcurrencyStamp))
            .ToArrayAsync(cancellationToken);
        requireCurrent();
        string[] keys = SupportAccessSettingGroup.SettingKeys.ToArray();
        var settings = await context.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey))
            .Select(row => new HumanSupportSettingFact(row.Id, row.SettingKey, row.Value, row.SettingValueTypeId,
                row.IsLocked, row.Description, row.Category, row.AllowedValues))
            .ToArrayAsync(cancellationToken);
        requireCurrent();
        return new(forwarding, sessions.ToImmutableArray(), settings.ToImmutableArray());
    }
}
