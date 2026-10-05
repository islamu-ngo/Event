using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations;

public sealed class EventDiscoverySnapshotReservationConfiguration : IEntityTypeConfiguration<EventDiscoverySnapshotReservation>
{
    public void Configure(EntityTypeBuilder<EventDiscoverySnapshotReservation> builder)
    {
        builder.ToTable("event_discovery_snapshot_reservations");
        builder.HasKey(reservation => reservation.TenantId);
        builder.Property(reservation => reservation.TenantId).ValueGeneratedNever();
        // No FK to public Tenant/source rows: native FK validation must not acquire their locks
        // during reservation bootstrap or after the terminal epoch when membership is inserted.
    }
}
