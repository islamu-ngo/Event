using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class UserLocationPrivacyErasureRepository
{
    // The caller owns the exact-saga, identity-fenced serializable settlement and tenant
    // inventory. No current role, purpose extension, decryptor or privacy-owner veto applies.
    private async Task ErasePreFindingInTenantAsync(Guid humanId, Guid tenantId, CancellationToken token)
    {
        var custody = dbContext.Set<ModerationPreFindingCustody>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.TenantId == tenantId
                && (row.ProposedByHumanId == humanId || row.PrivacyOwnerHumanId == humanId));
        var notices = dbContext.Set<ModerationPreFindingNotice>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.TenantId == tenantId
                && (row.RecipientHumanId == humanId || row.IssuedByHumanId == humanId
                    || row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == humanId
                    || custody.Any(owner => owner.Id == row.CustodyId)));
        var receipts = dbContext.Set<ModerationPreFindingReceipt>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.TenantId == tenantId);
        while (true)
        {
            var rows = await notices.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.CaseId, row.HostBindingId, row.TrustDomainId })
                .Take(100).ToArrayAsync(token);
            if (rows.Length == 0) break;
            var caseIds = rows.Select(row => row.CaseId).Distinct().Order().ToArray();
            foreach (var caseId in caseIds)
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(dbContext, caseId, token);
            foreach (var row in rows)
                if (!await dbContext.Set<ModerationPreFindingErasureTombstone>()
                    .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
                    .AnyAsync(marker => marker.CaseId == row.CaseId && marker.TenantId == tenantId, token))
                    dbContext.Add(ModerationPreFindingErasureTombstone.Record(
                        row.CaseId, row.HostBindingId, row.TrustDomainId, tenantId));
            // Save only inside the caller's still-uncommitted settlement transaction.
            // Erasure cannot remove the old opportunity and permit a restarted clock.
            await dbContext.SaveChangesAsync(token);
            await ErasePreFindingReceiptsAsync(receipts.Where(row => caseIds.Contains(row.CaseId)), token);
            var ids = rows.Select(row => row.Id).ToArray();
            foreach (var id in ids)
                await ModerationPreFindingNotificationCleanup.StageAsync(dbContext, tenantId, null, id, token);
            if (await notices.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(token) != ids.Length)
                throw new InvalidOperationException("Pre-finding erasure did not settle its selected notice batch.");
        }
        while (true)
        {
            var ids = await custody.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id).Take(100).ToArrayAsync(token);
            if (ids.Length == 0) break;
            await ErasePreFindingReceiptsAsync(receipts.Where(row => ids.Contains(row.ResultId)), token);
            if (await custody.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(token) != ids.Length)
                throw new InvalidOperationException("Pre-finding erasure did not settle its selected custody batch.");
        }
        await ErasePreFindingReceiptsAsync(receipts.Where(row => row.ActorHumanId == humanId), token);
    }

    private static async Task ErasePreFindingReceiptsAsync(
        IQueryable<ModerationPreFindingReceipt> receipts, CancellationToken token)
    {
        while (true)
        {
            var ids = await receipts.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id).Take(100).ToArrayAsync(token);
            if (ids.Length == 0) break;
            if (await receipts.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(token) != ids.Length)
                throw new InvalidOperationException("Pre-finding erasure did not settle its selected receipt batch.");
        }
    }
}
