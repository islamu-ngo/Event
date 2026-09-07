// and composite unique constraint on (TenantId, Key).

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.HasKey(e => e.Id);

        // UUID v7 generation for better index performance
        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        // Composite unique constraint - one override per setting per tenant
        builder.HasIndex(e => new { e.TenantId, e.SettingKey })
            .IsUnique();

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.SettingKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Value)
            .IsRequired();

        builder.Property(e => e.IsLocked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // Relationship to Tenant
        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
