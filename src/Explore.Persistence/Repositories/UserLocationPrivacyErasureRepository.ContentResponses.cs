using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class UserLocationPrivacyErasureRepository
{
    // Called only by the existing exact-saga, serializable, all-tenant privacy settlement.
    // No current Event role, host selection, original disclosure or decryption key is needed
    // to remove an erased representative's governed text and submission receipt.
    private async Task EraseContentResponsesInTenantAsync(Guid humanId, Guid tenantId, CancellationToken token)
    {
        var responses = dbContext.Set<ModerationContentResponse>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.TenantId == tenantId && row.RepresentativeHumanId == humanId);
        while (true)
        {
            var ids = await responses.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => row.Id).Take(100).ToArrayAsync(token);
            if (ids.Length == 0) break;
            if (await responses.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(token) != ids.Length)
                throw new InvalidOperationException("Content response erasure did not settle its selected batch.");
        }
    }
}
