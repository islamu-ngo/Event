using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class OrganizationTenantConfiguration : IEntityTypeConfiguration<OrganizationTenant>
{
    public void Configure(EntityTypeBuilder<OrganizationTenant> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });
        builder.HasAlternateKey(e => new { e.TenantId, e.OrganizationId });
        builder.Property(e => e.ApprovalStatusId).HasDefaultValue((int)ApprovalStatusEnum.Pending);
        builder.Property(e => e.DisplayNameOverride).HasMaxLength(500);
        builder.Property(e => e.DescriptionOverride).HasMaxLength(5000);
        builder.Property(e => e.WebsiteUrlOverride).HasMaxLength(2048);
        builder.Property(e => e.ContactEmailOverride).HasMaxLength(500);
        builder.Property(e => e.ModerationNote).HasMaxLength(2000);
        ConfigureAppearance(builder);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Organization).WithMany(e => e.TenantParticipations).HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ApprovalStatus).WithMany().HasForeignKey(e => e.ApprovalStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ProfilePicture).WithMany().HasForeignKey(e => e.ProfilePictureId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.BannerPicture).WithMany().HasForeignKey(e => e.BannerPictureId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.BackgroundImage).WithMany().HasForeignKey(e => e.BackgroundImageId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.TenantId, e.OrganizationId }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(e => new { e.TenantId, e.IsDeleted, e.ApprovalStatusId });
    }

    private static void ConfigureAppearance(EntityTypeBuilder<OrganizationTenant> builder)
    {
        builder.Property(e => e.BackgroundColor).HasMaxLength(50);
        builder.Property(e => e.BackgroundEffect).HasMaxLength(50);
        builder.Property(e => e.BannerColor).HasMaxLength(50);
    }
}

public sealed class GroupTenantConfiguration : IEntityTypeConfiguration<GroupTenant>
{
    public void Configure(EntityTypeBuilder<GroupTenant> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });
        builder.HasAlternateKey(e => new { e.TenantId, e.GroupId });
        builder.Property(e => e.ApprovalStatusId).HasDefaultValue((int)ApprovalStatusEnum.Pending);
        builder.Property(e => e.DisplayNameOverride).HasMaxLength(500);
        builder.Property(e => e.DescriptionOverride).HasMaxLength(5000);
        builder.Property(e => e.ModerationNote).HasMaxLength(2000);
        builder.Property(e => e.BackgroundColor).HasMaxLength(50);
        builder.Property(e => e.BackgroundEffect).HasMaxLength(50);
        builder.Property(e => e.BannerColor).HasMaxLength(50);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Group).WithMany(e => e.TenantParticipations).HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ApprovalStatus).WithMany().HasForeignKey(e => e.ApprovalStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ProfilePicture).WithMany().HasForeignKey(e => e.ProfilePictureId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.BannerPicture).WithMany().HasForeignKey(e => e.BannerPictureId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.BackgroundImage).WithMany().HasForeignKey(e => e.BackgroundImageId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ParentOrganizationTenant)
            .WithMany(e => e.ChildGroups)
            .HasForeignKey(e => new { e.TenantId, e.ParentOrganizationTenantId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ParentGroupTenant)
            .WithMany(e => e.ChildGroups)
            .HasForeignKey(e => new { e.TenantId, e.ParentGroupTenantId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.GroupId }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(e => new { e.TenantId, e.IsDeleted, e.ApprovalStatusId });
        builder.HasIndex(e => new { e.TenantId, e.ParentOrganizationTenantId });
        builder.HasIndex(e => new { e.TenantId, e.ParentGroupTenantId });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_group_tenants_parent_exclusive", "parent_organization_tenant_id IS NULL OR parent_group_tenant_id IS NULL");
            t.HasCheckConstraint("ck_group_tenants_no_self_parent", "parent_group_tenant_id IS NULL OR parent_group_tenant_id <> id");
        });
    }
}
