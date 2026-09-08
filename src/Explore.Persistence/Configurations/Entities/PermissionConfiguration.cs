using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.ResourceKind)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Action)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FieldScope)
            .HasMaxLength(100);

        builder.Property(e => e.MasterCode)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.GroupName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Ignore(e => e.Scope);

        builder.Property(e => e.RoleScopeId)
            .IsRequired();

        builder.HasOne(e => e.RoleScope)
            .WithMany()
            .HasForeignKey(e => e.RoleScopeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.IsSystem)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsFiltered)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // Unique MasterCode (e.g., "event:update:description")
        builder.HasIndex(e => e.MasterCode)
            .IsUnique();

        // Fast lookup by resource kind (e.g., all "event" permissions)
        builder.HasIndex(e => new { e.ResourceKind, e.Action });

        // Filter by scope for capability ceiling queries
        builder.HasIndex(e => e.RoleScopeId);
    }
}
