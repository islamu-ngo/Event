using Explore.Domain;
using Explore.Persistence.Schema;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AtprotoFederationTenantSettingConfiguration : IEntityTypeConfiguration<AtprotoFederationTenantSetting>
{
    public void Configure(EntityTypeBuilder<AtprotoFederationTenantSetting> builder)
    {
        builder.ToTable("atproto_federation_tenant_settings", table =>
        {
            table.HasCheckConstraint("ck_atproto_federation_tenant_settings_key",
                AtprotoFederationInstanceSettingConfiguration.KeyConstraintSql);
            table.HasCheckConstraint("ck_atproto_federation_tenant_settings_value",
                AtprotoFederationInstanceSettingConfiguration.ValueConstraintSql);
        });
        builder.HasKey(setting => setting.Id);
        builder.Property(setting => setting.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(setting => setting.TenantId).IsRequired();
        builder.HasIndex(setting => new { setting.TenantId, setting.SettingKey }).IsUnique();
        builder.Property(setting => setting.SettingKey)
            .IsRequired().HasMaxLength(42).IsUnicode(false).UsePortableOrdinalAscii();
        builder.Property(setting => setting.Value)
            .IsRequired().HasMaxLength(15).IsUnicode(false).UsePortableOrdinalAscii();
        builder.Property(setting => setting.IsLocked).IsRequired();
        builder.Property(setting => setting.CreatedAt).IsRequired().HasDefaultValueSql("NOW()");
        builder.HasOne(setting => setting.Tenant).WithMany()
            .HasForeignKey(setting => setting.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
