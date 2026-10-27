using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationConsumerInstallationConfiguration : IEntityTypeConfiguration<ModerationConsumerInstallation>
{
    public void Configure(EntityTypeBuilder<ModerationConsumerInstallation> builder)
    {
        builder.ToTable("moderation_consumer_installations", table =>
        {
            table.HasCheckConstraint("ck_moderation_consumer_installation_owner", "consumer_id <> '' AND mode_code = 1");
            table.HasCheckConstraint("ck_moderation_consumer_installation_generation", "writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_consumer_installation_validity", "valid_until > installed_at");
            table.HasCheckConstraint("ck_moderation_consumer_installation_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= installed_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ConsumerId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.RuntimeGeneration).HasMaxLength(64).IsRequired();
        builder.Property(row => row.InventoryFingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.ConsumerId, row.ModeCode, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.InstalledByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationConsumerInstallation.Revision)
                or nameof(ModerationConsumerInstallation.WithdrawnAt) or nameof(ModerationConsumerInstallation.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
