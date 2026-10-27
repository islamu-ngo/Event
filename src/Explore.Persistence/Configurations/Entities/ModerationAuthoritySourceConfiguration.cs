using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationAuthoritySourceConfiguration :
    IEntityTypeConfiguration<ModerationHostBinding>,
    IEntityTypeConfiguration<ModerationQualificationEpoch>
{
    public void Configure(EntityTypeBuilder<ModerationHostBinding> builder)
    {
        builder.ToTable("moderation_host_bindings", table =>
            table.HasCheckConstraint("ck_moderation_host_binding_slot", "installation_slot = 1"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property<int>("InstallationSlot").HasDefaultValue(1);
        builder.HasIndex("InstallationSlot").IsUnique();
        builder.HasOne<ServicePrincipal>().WithMany()
            .HasForeignKey(entity => entity.MaintenanceServicePrincipalId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ModerationQualificationEpoch> builder)
    {
        builder.ToTable("moderation_qualification_epochs", table =>
            table.HasCheckConstraint("ck_moderation_qualification_revision", "revision > 0"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.HasIndex(entity => new { entity.HostBindingId, entity.TenantId }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany()
            .HasForeignKey(entity => entity.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(entity => entity.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
