using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventAddOnCatalogItemConfiguration :
    IEntityTypeConfiguration<EventAddOnCatalogItem>
{
    public void Configure(EntityTypeBuilder<EventAddOnCatalogItem> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_event_add_on_catalog_items_money",
                "unit_price_minor >= 0");
            table.HasCheckConstraint(
                "ck_event_add_on_catalog_items_capacity",
                "inventory_capacity > 0");
        });
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Name)
            .IsRequired()
            .HasMaxLength(EventAddOnCatalogItem.MaxNameLength);
        builder.Property(item => item.Description)
            .HasMaxLength(EventAddOnCatalogItem.MaxDescriptionLength);
        builder.Property(item => item.UnitPriceMinor).HasColumnType("bigint");
        builder.Property(item => item.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(item => item.FulfillmentDisclosure)
            .IsRequired()
            .HasMaxLength(EventAddOnCatalogItem.MaxDisclosureLength);
        builder.Property(item => item.RefundDisclosure)
            .IsRequired()
            .HasMaxLength(EventAddOnCatalogItem.MaxDisclosureLength);
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(item => new { item.TenantId, item.Id });
        builder.HasAlternateKey(item => new
        {
            item.TenantId,
            item.EventAddOnCatalogVersionId,
            item.Id,
        });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventAddOnCatalogVersion>()
            .WithMany(catalog => catalog.Items)
            .HasForeignKey(item => new { item.TenantId, item.EventAddOnCatalogVersionId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new
        {
            item.TenantId,
            item.EventAddOnCatalogVersionId,
            item.Id,
        })
            .IsUnique();
        builder.HasIndex(item => new
        {
            item.TenantId,
            item.EventAddOnCatalogVersionId,
            item.Name,
        })
            .IsUnique();
    }
}
