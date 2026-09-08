using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class CustomPropertyProjectionDirtyScopeConfiguration : IEntityTypeConfiguration<CustomPropertyProjectionDirtyScope>
{
    public void Configure(EntityTypeBuilder<CustomPropertyProjectionDirtyScope> builder)
    {

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .UseIdentityAlwaysColumn();

        builder.Property(e => e.ProjectionName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.ScopeType)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.Reason)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new
        {
            e.ProjectionName,
            e.ProjectionVersion,
            e.TenantId,
            e.ScopeType,
            e.ScopeId,
            e.DefinitionId,
        })
            .HasDatabaseName("ix_dirty_scope_unique")
            .IsUnique();

        builder.HasIndex(e => new { e.ProjectionName, e.ProjectionVersion, e.TenantId })
            .HasDatabaseName("ix_dirty_scope_pending")
            .HasFilter("drained_at IS NULL");
    }
}
