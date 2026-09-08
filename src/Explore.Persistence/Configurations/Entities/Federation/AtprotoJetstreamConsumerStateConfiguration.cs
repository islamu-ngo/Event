using Explore.Domain.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities.Federation;

public sealed class AtprotoJetstreamConsumerStateConfiguration
    : IEntityTypeConfiguration<AtprotoJetstreamConsumerState>
{
    public void Configure(EntityTypeBuilder<AtprotoJetstreamConsumerState> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_atproto_jetstream_cursor", "jetstream_cursor >= 0");
            table.HasCheckConstraint("ck_atproto_jetstream_lease_fence", "lease_fence >= 0");
            table.HasCheckConstraint(
                "ck_atproto_jetstream_lease_shape",
                "(lease_owner IS NULL AND lease_token IS NULL AND lease_expires_at IS NULL) OR " +
                "(lease_owner IS NOT NULL AND btrim(lease_owner) <> '' AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL)");
        });
        builder.Property(value => value.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(value => value.Cursor).HasColumnName("jetstream_cursor");
        builder.Property(value => value.Service).HasMaxLength(500).IsRequired();
        builder.Property(value => value.LeaseOwner).HasMaxLength(200);
        builder.Property(value => value.LeaseFence).IsConcurrencyToken();
        builder.HasIndex(value => value.Service)
            .IsUnique();
    }
}
