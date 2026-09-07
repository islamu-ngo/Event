using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationOrderLineConfiguration : IEntityTypeConfiguration<RegistrationOrderLine>
{
    public void Configure(EntityTypeBuilder<RegistrationOrderLine> builder)
    {
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.CurrencyCodeSnapshot).IsRequired().HasMaxLength(3);
        builder.Property(line => line.TicketTypeNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(line => line.UnitPriceAmountSnapshot).HasColumnType("bigint");
        builder.Property(line => line.ChosenUnitPriceAmountSnapshot).HasColumnType("bigint");
        builder.Property(line => line.LineSubtotalSnapshot).HasColumnType("bigint");
        builder.Property(line => line.PreDiscountLineSubtotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(line => line.PromotionDiscountAmountMinorSnapshot).HasColumnType("bigint");
        builder.Property(line => line.PostDiscountLineSubtotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(line => line.MinimumPriceAmountSnapshot).HasColumnType("bigint");
        builder.Property(line => line.SuggestedPriceAmountSnapshot).HasColumnType("bigint");
        builder.Property(line => line.CreatedAt).IsRequired();
        builder.Property(line => line.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(line => new { line.TenantId, line.Id });
        builder.HasAlternateKey(line => new { line.TenantId, line.RegistrationOrderId, line.Id });
        builder.HasAlternateKey(line => new
        {
            line.TenantId,
            line.RegistrationOrderId,
            line.Id,
            line.TicketTypeId
        });
        builder.HasOne<EventTicketCatalogVersion>().WithMany().HasForeignKey(line => new { line.TenantId, line.TicketCatalogVersionId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketType>().WithMany().HasForeignKey(line => new { line.TenantId, line.TicketTypeId })
            .HasPrincipalKey(ticketType => new { ticketType.TenantId, ticketType.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(line => new { line.TenantId, line.RegistrationOrderId, line.TicketTypeId }).IsUnique();
    }
}
