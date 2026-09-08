using Explore.Domain.Modules;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TenantCapabilityConfiguration : IEntityTypeConfiguration<TenantCapability>
{
    public void Configure(EntityTypeBuilder<TenantCapability> builder)
    {
        builder.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(c => c.ConfigurationJson).HasColumnType("jsonb");

        // Unique constraint: one capability per tenant-module pair
        builder.HasIndex(c => new { c.TenantId, c.ModuleId }).IsUnique();

        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Module)
            .WithMany()
            .HasForeignKey(c => c.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // NOTE: Business entity seed data moved to DatabaseSeeder for conditional (Development-only) seeding.
        // See Explore.Persistence/Seed/DatabaseSeeder.cs and SeedData.cs
    }
}
