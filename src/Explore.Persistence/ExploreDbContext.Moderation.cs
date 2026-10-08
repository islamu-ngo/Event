using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    internal Guid? ModerationTrustDomainId { get; set; }

    public DbSet<ModerationCase> ModerationCases => Set<ModerationCase>();
    public DbSet<ModerationDecision> ModerationDecisions => Set<ModerationDecision>();
    public DbSet<ModerationSanction> ModerationSanctions => Set<ModerationSanction>();
    public DbSet<ModerationProtectiveMeasure> ModerationProtectiveMeasures => Set<ModerationProtectiveMeasure>();
    public DbSet<ModerationOperationReceipt> ModerationOperationReceipts => Set<ModerationOperationReceipt>();
    public DbSet<ModerationEffectIntent> ModerationEffectIntents => Set<ModerationEffectIntent>();
    public DbSet<ModerationHostBinding> ModerationHostBindings => Set<ModerationHostBinding>();
    public DbSet<ModerationQualificationEpoch> ModerationQualificationEpochs => Set<ModerationQualificationEpoch>();

    private void ApplyModerationQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationQualificationEpoch>().HasQueryFilter("ModerationQualification",
            entity => entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCase>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationDecision>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationSanction>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationProtectiveMeasure>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationOperationReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId);
        builder.Entity<ModerationEffectIntent>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId);
    }
}
