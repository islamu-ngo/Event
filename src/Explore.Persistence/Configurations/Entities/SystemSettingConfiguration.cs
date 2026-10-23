// and unique constraint on Key.

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_system_settings_no_dedicated_atproto_federation",
            "setting_key NOT IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled', 'federation.atproto_events_backfill_mode')"));
        builder.HasKey(e => e.Id);

        // UUID v7 generation for better index performance
        builder.Property(e => e.Id)
            .HasDefaultValueSql("uuidv7()");

        // Unique constraint on setting key
        builder.HasIndex(e => e.SettingKey)
            .IsUnique();

        builder.Property(e => e.SettingKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Value)
            .IsRequired();

        builder.Ignore(e => e.ValueType);

        builder.Property(e => e.SettingValueTypeId)
            .IsRequired();

        builder.HasOne(e => e.SettingValueTypeLookup)
            .WithMany()
            .HasForeignKey(e => e.SettingValueTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.IsLocked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.AllowedValues)
            .HasColumnType("jsonb");

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Category)
            .HasMaxLength(100);

        builder.Property(e => e.DisplayOrder)
            .HasDefaultValue(0);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // Seed initial system settings using centralized SeedIds
    }
}
