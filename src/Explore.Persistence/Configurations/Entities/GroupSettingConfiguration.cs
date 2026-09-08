// and composite unique constraint on (GroupId, SettingKey).

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class GroupSettingConfiguration : IEntityTypeConfiguration<GroupSetting>
{
    public void Configure(EntityTypeBuilder<GroupSetting> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.HasIndex(e => new { e.GroupTenantId, e.SettingKey })
            .IsUnique();

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.Property(e => e.GroupTenantId)
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

        builder.HasOne(e => e.GroupTenant)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => new { e.TenantId, e.GroupTenantId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
