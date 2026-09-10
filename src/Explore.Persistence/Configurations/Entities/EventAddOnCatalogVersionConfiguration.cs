using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventAddOnCatalogVersionConfiguration :
    IEntityTypeConfiguration<EventAddOnCatalogVersion>
{
    public void Configure(EntityTypeBuilder<EventAddOnCatalogVersion> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_event_add_on_catalog_versions_lifecycle",
            "retired_at IS NULL OR (published_at IS NOT NULL AND retired_at >= published_at)"));
        builder.Property(catalog => catalog.Id).ValueGeneratedNever();
        builder.Property(catalog => catalog.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(catalog => catalog.CreatedAt).IsRequired();
        builder.Property(catalog => catalog.IsDeleted).HasDefaultValue(false);
        builder.Property(catalog => catalog.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(catalog => new { catalog.TenantId, catalog.Id });
        builder.HasAlternateKey(catalog => new { catalog.TenantId, catalog.EventId, catalog.Id });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(catalog => catalog.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(catalog => new { catalog.TenantId, catalog.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(catalog => catalog.Items)
            .WithOne()
            .HasForeignKey(item => new { item.TenantId, item.EventAddOnCatalogVersionId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(catalog => new
        {
            catalog.TenantId,
            catalog.EventId,
            catalog.VersionNumber,
        })
            .IsUnique();
        builder.HasIndex(catalog => new { catalog.TenantId, catalog.EventId })
            .IsUnique()
            .HasFilter("published_at IS NOT NULL AND retired_at IS NULL AND is_deleted = false");
    }
}
