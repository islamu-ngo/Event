// and composite unique constraint on (OrganizationId, SettingKey).

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class OrganizationSettingConfiguration : IEntityTypeConfiguration<OrganizationSetting>
{
    public void Configure(EntityTypeBuilder<OrganizationSetting> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.HasIndex(e => new { e.OrganizationTenantId, e.SettingKey })
            .IsUnique();

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.OrganizationTenantId)
            .IsRequired();

        builder.Property(e => e.SettingKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.Value)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.OrganizationTenant)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => new { e.TenantId, e.OrganizationTenantId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
