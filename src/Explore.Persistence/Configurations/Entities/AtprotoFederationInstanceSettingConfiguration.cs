using Explore.Domain;
using Explore.Persistence.Schema;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AtprotoFederationInstanceSettingConfiguration : IEntityTypeConfiguration<AtprotoFederationInstanceSetting>
{
    internal const string KeyConstraintSql = """
        (setting_key = 'federation.atproto_events_enabled' AND length(setting_key) = 33)
        OR (setting_key = 'federation.atproto_events_backfill_enabled' AND length(setting_key) = 42)
        OR (setting_key = 'federation.atproto_events_backfill_mode' AND length(setting_key) = 39)
        """;

    internal const string ValueConstraintSql = """
        (setting_key IN ('federation.atproto_events_enabled', 'federation.atproto_events_backfill_enabled')
            AND ((value = 'true' AND length(value) = 4) OR (value = 'false' AND length(value) = 5)))
        OR (setting_key = 'federation.atproto_events_backfill_mode'
            AND ((lower(value) = '"downtime_only"' AND length(value) = 15)
                OR (lower(value) = '"full"' AND length(value) = 6)))
        """;

    public void Configure(EntityTypeBuilder<AtprotoFederationInstanceSetting> builder)
    {
        builder.ToTable("atproto_federation_instance_settings", table =>
        {
            table.HasCheckConstraint("ck_atproto_federation_instance_settings_key", KeyConstraintSql);
            table.HasCheckConstraint("ck_atproto_federation_instance_settings_value", ValueConstraintSql);
        });
        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasIndex(setting => setting.SettingKey).IsUnique();
        builder.Property(setting => setting.SettingKey)
            .IsRequired().HasMaxLength(42).IsUnicode(false).UsePortableOrdinalAscii();
        builder.Property(setting => setting.Value)
            .IsRequired().HasMaxLength(15).IsUnicode(false).UsePortableOrdinalAscii();
        builder.Property(setting => setting.IsLocked).IsRequired();
        builder.Property(setting => setting.CreatedAt).IsRequired().HasDefaultValueSql("NOW()");
    }
}
