using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Schema;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ManagedTenantProvisioningPolicySettingConfiguration
    : IEntityTypeConfiguration<ManagedTenantProvisioningPolicySetting>
{
    public void Configure(EntityTypeBuilder<ManagedTenantProvisioningPolicySetting> builder)
    {
        builder.ToTable("managed_tenant_provisioning_policy_settings", table =>
        {
            table.HasCheckConstraint("ck_provisioning_policy_revision", "revision >= 0");
            table.HasCheckConstraint("ck_provisioning_policy_absence",
                "is_present = true OR (is_locked = false AND value IS NULL)");
            table.HasCheckConstraint("ck_provisioning_policy_value",
                "value IS NULL OR setting_key IN ("
                + string.Join(",", ManagedTenantProvisioningPolicyInventory.ValueKeys.Select(key => "'" + key + "'")) + ")");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(row => row.SettingKey).IsRequired().HasMaxLength(256)
            .IsUnicode(false).UsePortableOrdinalAscii();
        builder.HasIndex(row => row.SettingKey).IsUnique();
        builder.Property(row => row.IsPresent).IsRequired();
        builder.Property(row => row.IsLocked).IsRequired();
        builder.Property(row => row.Value).IsRequired(false);
        builder.Property(row => row.Revision).IsRequired().IsConcurrencyToken();
    }
}
