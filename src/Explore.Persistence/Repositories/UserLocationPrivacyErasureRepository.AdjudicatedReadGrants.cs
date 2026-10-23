using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class UserLocationPrivacyErasureRepository
{
    // Only the existing exact-saga, Human-fenced serializable settlement calls this per RLS tenant.
    private async Task EraseAdjudicatedReadGrantsInTenantAsync(Guid humanId, Guid tenantId, CancellationToken token)
    {
        var grants = dbContext.ModerationAdjudicatedCaseReadGrants
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.TenantId == tenantId
                && (row.RecipientUserId == humanId || row.IssuerUserId == humanId || row.RevokedByUserId == humanId));
        while (true)
        {
            var ids = await grants.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id).Take(100).ToArrayAsync(token);
            if (ids.Length == 0) break;
            if (await grants.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(token) != ids.Length)
                throw new InvalidOperationException("Adjudicated-case read grant erasure did not settle its selected batch.");
            // Bulk deletion bypasses tracking; forget only disposed rows, never unrelated pending work.
            foreach (var entry in dbContext.ChangeTracker.Entries<ModerationAdjudicatedCaseReadGrant>()
                .Where(entry => ids.Contains(entry.Entity.Id) && entry.Entity.TenantId == tenantId
                    && (entry.Entity.RecipientUserId == humanId || entry.Entity.IssuerUserId == humanId
                        || entry.Entity.RevokedByUserId == humanId)).ToArray())
                entry.State = EntityState.Detached;
        }
    }
}
