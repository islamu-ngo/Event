using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationOrderAddOnLineConfiguration :
    IEntityTypeConfiguration<RegistrationOrderAddOnLine>
{
    public void Configure(EntityTypeBuilder<RegistrationOrderAddOnLine> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_registration_order_add_on_lines_quantity",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_registration_order_add_on_lines_money",
                "unit_price_minor_snapshot >= 0 AND line_total_minor_snapshot >= 0");
        });
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.NameSnapshot)
            .IsRequired()
            .HasMaxLength(EventAddOnCatalogItem.MaxNameLength);
        builder.Property(line => line.UnitPriceMinorSnapshot).HasColumnType("bigint");
        builder.Property(line => line.LineTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(line => line.CurrencyCodeSnapshot).IsRequired().HasMaxLength(3);
        builder.Property(line => line.FulfillmentDisclosureSnapshot)
            .IsRequired()
            .HasMaxLength(EventAddOnCatalogItem.MaxDisclosureLength);
        builder.Property(line => line.RefundDisclosureSnapshot)
            .IsRequired()
            .HasMaxLength(EventAddOnCatalogItem.MaxDisclosureLength);
        builder.Property(line => line.CreatedAt).IsRequired();
        builder.Property(line => line.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(line => new { line.TenantId, line.Id });
        builder.HasAlternateKey(line => new
        {
            line.TenantId,
            line.EventId,
            line.RegistrationOrderId,
            line.Id,
        });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(line => line.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventAddOnCatalogVersion>()
            .WithMany()
            .HasForeignKey(line => new
            {
                line.TenantId,
                line.EventId,
                line.EventAddOnCatalogVersionId,
            })
            .HasPrincipalKey(catalog => new
            {
                catalog.TenantId,
                catalog.EventId,
                catalog.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventAddOnCatalogItem>()
            .WithMany()
            .HasForeignKey(line => new
            {
                line.TenantId,
                line.EventAddOnCatalogVersionId,
                line.EventAddOnCatalogItemId,
            })
            .HasPrincipalKey(item => new
            {
                item.TenantId,
                item.EventAddOnCatalogVersionId,
                item.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new
        {
            line.TenantId,
            line.RegistrationOrderId,
            line.EventAddOnCatalogItemId,
        })
            .IsUnique();
    }
}
