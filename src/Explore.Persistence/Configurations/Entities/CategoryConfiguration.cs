using Explore.Domain;
using Explore.Persistence.Seed;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.Property(e => e.MasterCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();

        builder.HasOne(e => e.Parent)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ParentId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.MasterCode })
            .IsUnique()
            .HasDatabaseName("ix_categories_tenant_master_code");

        // NOTE: Business entity seed data moved to DatabaseSeeder for conditional (Development-only) seeding.
        // See Explore.Persistence/Seed/DatabaseSeeder.cs and SeedData.cs
    }
}
