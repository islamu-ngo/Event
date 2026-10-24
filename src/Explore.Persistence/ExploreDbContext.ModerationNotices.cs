using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    public DbSet<ModerationNoticeSource> ModerationNoticeSources => Set<ModerationNoticeSource>();
    public DbSet<ModerationNotice> ModerationNotices => Set<ModerationNotice>();

    // Recipient authorization belongs to the portal read owner; these mandatory fences
    // never grant subject/reviewer access.
    private void ApplyModerationNoticeQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationContentResponse>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationNoticeSource>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && (row.TenantId == null || row.TenantId == TenantFilterTenantId));
        builder.Entity<ModerationNotice>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && (row.TenantId == null || row.TenantId == TenantFilterTenantId));
    }
}
