using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    internal Guid? ModerationTrustDomainId { get; set; }

    public DbSet<ModerationCase> ModerationCases => Set<ModerationCase>();
    public DbSet<ModerationFinding> ModerationFindings => Set<ModerationFinding>();
    public DbSet<ModerationReviewerAssignment> ModerationReviewerAssignments => Set<ModerationReviewerAssignment>();
    public DbSet<ModerationPolicy> ModerationPolicies => Set<ModerationPolicy>();
    public DbSet<ModerationPolicyRule> ModerationPolicyRules => Set<ModerationPolicyRule>();
    public DbSet<ModerationDecision> ModerationDecisions => Set<ModerationDecision>();
    public DbSet<ModerationSanction> ModerationSanctions => Set<ModerationSanction>();
    public DbSet<ModerationProtectiveMeasure> ModerationProtectiveMeasures => Set<ModerationProtectiveMeasure>();
    public DbSet<ModerationOperationReceipt> ModerationOperationReceipts => Set<ModerationOperationReceipt>();
    public DbSet<ModerationEffectIntent> ModerationEffectIntents => Set<ModerationEffectIntent>();
    public DbSet<ModerationHostBinding> ModerationHostBindings => Set<ModerationHostBinding>();
    public DbSet<ModerationQualificationEpoch> ModerationQualificationEpochs => Set<ModerationQualificationEpoch>();
    public DbSet<ModerationQualificationEvidence> ModerationQualificationEvidence => Set<ModerationQualificationEvidence>();
    public DbSet<ModerationQualificationMutationReceipt> ModerationQualificationMutationReceipts => Set<ModerationQualificationMutationReceipt>();

    private void ApplyModerationQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationQualificationEpoch>().HasQueryFilter("ModerationQualification",
            entity => entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationQualificationEvidence>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationQualificationMutationReceipt>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && entity.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationCase>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationFinding>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.ScopeTrustDomainId == ModerationTrustDomainId
                && (entity.ScopeTenantId == null || entity.ScopeTenantId == TenantFilterTenantId));
        builder.Entity<ModerationReviewerAssignment>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.TrustDomainId == ModerationTrustDomainId
                && (entity.CaseTenantId == null || entity.CaseTenantId == TenantFilterTenantId));
        builder.Entity<ModerationPolicy>().HasQueryFilter("ModerationAuthority",
            entity => ModerationTrustDomainId != null && entity.JurisdictionTrustDomainId == ModerationTrustDomainId
                && (entity.JurisdictionTenantId == null || entity.JurisdictionTenantId == TenantFilterTenantId));
        builder.Entity<ModerationPolicyRule>().HasQueryFilter("ModerationAuthority",
            entity => ModerationPolicies.Any(policy => policy.Id == entity.PolicyId));
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
