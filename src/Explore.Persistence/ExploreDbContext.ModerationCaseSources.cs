using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    public DbSet<ModerationCaseTargetSource> ModerationCaseTargetSources => Set<ModerationCaseTargetSource>();
    public DbSet<ModerationCaseTargetPolicy> ModerationCaseTargetPolicies => Set<ModerationCaseTargetPolicy>();
    public DbSet<ModerationCaseReadGrant> ModerationCaseReadGrants => Set<ModerationCaseReadGrant>();
    public DbSet<ModerationAdjudicatedCaseReadGrant> ModerationAdjudicatedCaseReadGrants => Set<ModerationAdjudicatedCaseReadGrant>();
    public DbSet<ModerationCaseReadGrantRevocationReceipt> ModerationCaseReadGrantRevocationReceipts => Set<ModerationCaseReadGrantRevocationReceipt>();
    public DbSet<ModerationCaseAttributionSource> ModerationCaseAttributionSources => Set<ModerationCaseAttributionSource>();
    public DbSet<ModerationCaseAttributionHead> ModerationCaseAttributionHeads => Set<ModerationCaseAttributionHead>();
    public DbSet<ModerationCaseAttributionRenewalReceipt> ModerationCaseAttributionRenewalReceipts => Set<ModerationCaseAttributionRenewalReceipt>();

    private void ApplyModerationCaseSourceQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationAdjudicatedCaseReadGrant>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseReadGrantRevocationReceipt>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseAttributionRenewalReceipt>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseAttributionHead>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseAttributionSource>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseTargetSource>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseTargetPolicy>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCaseReadGrant>().HasQueryFilter("ModerationAuthority",
            row => ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
    }
}
