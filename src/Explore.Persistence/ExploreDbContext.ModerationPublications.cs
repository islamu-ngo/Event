using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    public DbSet<ModerationPolicyPublication> ModerationPolicyPublications => Set<ModerationPolicyPublication>();
    public DbSet<ModerationPolicySelection> ModerationPolicySelections => Set<ModerationPolicySelection>();
    public DbSet<ModerationPolicyPublicationCandidate> ModerationPolicyPublicationCandidates => Set<ModerationPolicyPublicationCandidate>();

    private void ApplyModerationPublicationQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationPolicyPublicationCandidate>().HasQueryFilter("ModerationAuthority", row =>
            ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
                && row.TenantId == TenantFilterTenantId);
        builder.Entity<ModerationPolicyPublication>().HasQueryFilter("ModerationAuthority", row =>
            ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
            && TenantFilterTenantId != null
            && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == TenantFilterTenantId));
        builder.Entity<ModerationPolicySelection>().HasQueryFilter("ModerationAuthority", row =>
            ModerationTrustDomainId != null && row.TrustDomainId == ModerationTrustDomainId
            && TenantFilterTenantId != null
            && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == TenantFilterTenantId));
        builder.Entity<ModerationPolicyPublicationClass>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationReason>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationMode>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationProfile>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationScope>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationSubject>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationEffect>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationPlan>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationConsumer>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationPrerequisite>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
        builder.Entity<ModerationPolicyPublicationEvidence>().HasQueryFilter("ModerationAuthority",
            row => ModerationPolicyPublications.Any(publication => publication.Id == row.PublicationId));
    }
}
