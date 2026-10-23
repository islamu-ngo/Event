using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationRecoveryStoreStateConfiguration : IEntityTypeConfiguration<ModerationRecoveryStoreState>
{
    public void Configure(EntityTypeBuilder<ModerationRecoveryStoreState> builder)
    {
        builder.ToTable("moderation_recovery_store_states", table =>
        {
            table.HasCheckConstraint("ck_moderation_recovery_store_slot", "installation_slot = 1");
            table.HasCheckConstraint("ck_moderation_recovery_store_coordinates",
                "revision > 0 AND generation > 0 AND generation <= revision - position AND position >= 0 AND position < revision");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property<int>("InstallationSlot").HasDefaultValue(1);
        builder.HasIndex("InstallationSlot").IsUnique();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.Generation).IsConcurrencyToken();
        builder.Property(row => row.Position).IsConcurrencyToken();
    }
}
