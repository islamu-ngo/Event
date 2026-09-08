using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class OrganizationTenantEvidenceConfiguration
    : IEntityTypeConfiguration<OrganizationTenantEvidence>
{
    public void Configure(EntityTypeBuilder<OrganizationTenantEvidence> builder)
    {
        builder.Property(row => row.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(row => new { row.TenantId, row.Id });
        builder.Property(row => row.ReviewStatusId).HasDefaultValue((int)ApprovalStatusEnum.Pending);
        builder.Property(row => row.ReviewNotes).HasMaxLength(2000);
        builder.Property(row => row.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(row => row.Tenant)
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.OrganizationTenant)
            .WithMany(participation => participation.LegitimacyEvidence)
            .HasForeignKey(row => new { row.TenantId, row.OrganizationTenantId })
            .HasPrincipalKey(participation => new { participation.TenantId, participation.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.DocumentStorageObject)
            .WithMany()
            .HasForeignKey(row => new { row.TenantId, row.DocumentStorageObjectId })
            .HasPrincipalKey(storageObject => new { storageObject.TenantId, storageObject.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.ReviewStatus)
            .WithMany()
            .HasForeignKey(row => row.ReviewStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.ReviewedByUser)
            .WithMany()
            .HasForeignKey(row => row.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(row => new
        {
            row.TenantId,
            row.OrganizationTenantId,
            row.DocumentStorageObjectId
        })
            .IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.OrganizationTenantId, row.ReviewStatusId });
    }
}
