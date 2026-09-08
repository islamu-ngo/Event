using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Privacy.ErasureAuthority.Configurations;

public sealed class EmbeddedPrivacyErasureCounterConfiguration
    : IEntityTypeConfiguration<PrivacyErasureCounter>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureCounter> builder)
    {
        builder.ToTable(
            RelationalModelNamespace.Prefix + PrivacyErasureAuthorityDatabaseContract.CounterTable,
            table =>
        {
            table.HasCheckConstraint("ck_authority_counter_singleton", "singleton = 1");
            table.HasCheckConstraint("ck_authority_counter_nonnegative", "last_sequence >= 0");
            table.HasCheckConstraint(
                "ck_authority_counter_retained_floor",
                "retained_floor_sequence >= 0 AND retained_floor_sequence <= last_sequence");
        });
        builder.HasKey(item => item.Singleton);
        builder.Property(item => item.Singleton).ValueGeneratedNever();
        builder.Property(item => item.RetainedFloorSequence).HasDefaultValue(0L);
    }
}
