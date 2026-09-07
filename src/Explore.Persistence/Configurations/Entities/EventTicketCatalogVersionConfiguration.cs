using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventTicketCatalogVersionConfiguration : IEntityTypeConfiguration<EventTicketCatalogVersion>
{
    public void Configure(EntityTypeBuilder<EventTicketCatalogVersion> builder)
    {
        builder.Property(catalog => catalog.Id).ValueGeneratedNever();
        builder.Property(catalog => catalog.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(catalog => catalog.MerchantDisclosureText).HasMaxLength(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
        builder.Property(catalog => catalog.RefundPolicyDisclosureText).HasMaxLength(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
        builder.Property(catalog => catalog.SupportContactDisclosureText).HasMaxLength(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
        builder.Property(catalog => catalog.CreatedAt).IsRequired();
        builder.Property(catalog => catalog.IsDeleted).HasDefaultValue(false);
        builder.Property(catalog => catalog.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(catalog => new { catalog.TenantId, catalog.Id });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(catalog => catalog.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany(@event => @event.TicketCatalogVersions)
            .HasForeignKey(catalog => new { catalog.TenantId, catalog.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(catalog => catalog.TicketCatalogStatus).WithMany().HasForeignKey(catalog => catalog.TicketCatalogStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(catalog => catalog.TicketTypes).WithOne()
            .HasForeignKey(ticketType => new { ticketType.TenantId, ticketType.CatalogId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.Id }).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(catalog => new { catalog.TenantId, catalog.EventId, catalog.VersionNumber }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(catalog => new { catalog.TenantId, catalog.EventId }).IsUnique().HasFilter("ticket_catalog_status_id = 2 AND is_deleted = false");
    }
}
