using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private void ApplyPreFindingQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationPreFindingCustody>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationPreFindingNotice>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationPreFindingReceipt>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationPreFindingErasureTombstone>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
    }
}
