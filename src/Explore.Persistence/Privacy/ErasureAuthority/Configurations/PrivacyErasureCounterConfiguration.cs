using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Privacy.ErasureAuthority.Configurations;

public sealed class PrivacyErasureCounterConfiguration
    : IEntityTypeConfiguration<PrivacyErasureCounter>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureCounter> builder)
    {
        builder.ToTable(PrivacyErasureAuthorityDatabaseContract.CounterTable, table =>
        {
            table.HasCheckConstraint("ck_privacy_erasure_authority_counter_singleton", "singleton");
            table.HasCheckConstraint("ck_privacy_erasure_authority_counter_nonnegative", "last_sequence >= 0");
            table.HasCheckConstraint(
                "ck_privacy_erasure_authority_counter_retained_floor",
                "retained_floor_sequence >= 0 AND retained_floor_sequence <= last_sequence");
        });
        builder.HasKey(item => item.Singleton);
        builder.Property(item => item.Singleton).ValueGeneratedNever();
        builder.Property(item => item.RetainedFloorSequence).HasDefaultValue(0L);
    }
}
