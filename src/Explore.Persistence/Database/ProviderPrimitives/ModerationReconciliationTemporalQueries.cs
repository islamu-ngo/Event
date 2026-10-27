using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Supplies SQLite DateTimeOffset predicates while preserving composable native query filters.</summary>
internal static class ModerationReconciliationTemporalQueries
{
    internal static IQueryable<ModerationSanction> DueSanctions(
        ExploreDbContext context, ModerationHostBinding host, Guid tenantId, DateTimeOffset now) =>
        context.Database.ProviderName == RelationalNamedLock.SqliteProvider
            ? context.ModerationSanctions.FromSqlInterpolated($"""
                SELECT * FROM moderation_sanctions
                WHERE scope_trust_domain_id = {host.TrustDomainId}
                  AND scope_solution_id = {host.SolutionId} AND scope_instance_id = {host.InstanceId}
                  AND (scope_tenant_id = {tenantId} OR scope_kind = {(int)ModerationScopeKind.Instance})
                  AND ((state = {(int)ModerationSanctionState.Scheduled}
                        AND julianday(starts_at) <= julianday({now}))
                    OR (state IN ({(int)ModerationSanctionState.Scheduled}, {(int)ModerationSanctionState.Active})
                        AND ends_at IS NOT NULL AND julianday(ends_at) <= julianday({now})))
                """)
            : context.ModerationSanctions.Where(sanction =>
                sanction.State == ModerationSanctionState.Scheduled && sanction.StartsAt <= now
                || (sanction.State == ModerationSanctionState.Scheduled || sanction.State == ModerationSanctionState.Active)
                    && sanction.EndsAt != null && sanction.EndsAt <= now);

    internal static IQueryable<ModerationEffectIntent> RecoverableEffects(
        ExploreDbContext context, DateTimeOffset now) =>
        context.Database.ProviderName == RelationalNamedLock.SqliteProvider
            ? context.ModerationEffectIntents.FromSqlInterpolated($"""
                SELECT * FROM moderation_effect_intents
                WHERE completed_at IS NULL
                  AND (lease_expires_at IS NULL OR julianday(lease_expires_at) <= julianday({now}))
                """)
            : context.ModerationEffectIntents.Where(effect =>
                effect.CompletedAt == null && (effect.LeaseExpiresAt == null || effect.LeaseExpiresAt <= now));
}
