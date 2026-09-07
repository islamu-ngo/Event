using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventTicketTypeConfiguration : IEntityTypeConfiguration<EventTicketType>
{
    public void Configure(EntityTypeBuilder<EventTicketType> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_event_ticket_type_money_nonnegative",
            """
            (fixed_price_minor IS NULL OR fixed_price_minor >= 0)
            AND (minimum_price_minor IS NULL OR minimum_price_minor >= 0)
            AND (suggested_price_minor IS NULL OR suggested_price_minor >= 0)
            """));
        builder.Property(ticketType => ticketType.Id).ValueGeneratedNever();
        builder.Property(ticketType => ticketType.Name).IsRequired().HasMaxLength(200);
        builder.Property(ticketType => ticketType.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(ticketType => ticketType.FixedPriceMinor).HasColumnType("bigint");
        builder.Property(ticketType => ticketType.MinimumPriceMinor).HasColumnType("bigint");
        builder.Property(ticketType => ticketType.SuggestedPriceMinor).HasColumnType("bigint");
        builder.Property(ticketType => ticketType.CreatedAt).IsRequired();
        builder.Property(ticketType => ticketType.IsDeleted).HasDefaultValue(false);
        builder.Property(ticketType => ticketType.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(ticketType => new { ticketType.TenantId, ticketType.Id });

        builder.HasOne<EventTicketCatalogVersion>().WithMany(catalog => catalog.TicketTypes)
            .HasForeignKey(ticketType => new { ticketType.TenantId, ticketType.CatalogId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventCapacityPool>().WithMany()
            .HasForeignKey(ticketType => new { ticketType.TenantId, ticketType.CapacityPoolId })
            .HasPrincipalKey(pool => new { pool.TenantId, pool.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(ticketType => ticketType.TicketPricingMode).WithMany().HasForeignKey(ticketType => ticketType.TicketPricingModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(ticketType => ticketType.ParticipantDataCollectionMode).WithMany().HasForeignKey(ticketType => ticketType.ParticipantDataCollectionModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(ticketType => ticketType.Entitlements).WithOne()
            .HasForeignKey(entitlement => new { entitlement.TenantId, entitlement.TicketTypeId })
            .HasPrincipalKey(ticketType => new { ticketType.TenantId, ticketType.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
