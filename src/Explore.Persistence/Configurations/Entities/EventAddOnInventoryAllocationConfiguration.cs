using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventAddOnInventoryAllocationConfiguration :
    IEntityTypeConfiguration<EventAddOnInventoryAllocation>
{
    public void Configure(EntityTypeBuilder<EventAddOnInventoryAllocation> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_event_add_on_inventory_allocations_quantity",
            "quantity > 0 AND released_quantity >= 0 AND released_quantity <= quantity"));
        builder.Property(allocation => allocation.Id).ValueGeneratedNever();
        builder.Property(allocation => allocation.ReservedAt).IsRequired();
        builder.Property(allocation => allocation.CreatedAt).IsRequired();
        builder.Property(allocation => allocation.ConcurrencyStamp).IsConcurrencyToken();
        builder.Ignore(allocation => allocation.AllocatedQuantity);
        builder.HasAlternateKey(allocation => new { allocation.TenantId, allocation.Id });

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(allocation => allocation.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrderAddOnLine>()
            .WithMany()
            .HasForeignKey(allocation => new
            {
                allocation.TenantId,
                allocation.EventId,
                allocation.RegistrationOrderId,
                allocation.RegistrationOrderAddOnLineId,
            })
            .HasPrincipalKey(line => new
            {
                line.TenantId,
                line.EventId,
                line.RegistrationOrderId,
                line.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventAddOnCatalogItem>()
            .WithMany()
            .HasForeignKey(allocation => new
            {
                allocation.TenantId,
                allocation.EventAddOnCatalogItemId,
            })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(allocation => new { allocation.TenantId, allocation.OperationId })
            .IsUnique();
        builder.HasIndex(allocation => new
        {
            allocation.TenantId,
            allocation.RegistrationOrderAddOnLineId,
            allocation.ActiveUniquenessSlot,
        })
            .IsUnique()
            .HasFilter("active_uniqueness_slot IS NOT NULL");
        builder.HasIndex(allocation => new
        {
            allocation.TenantId,
            allocation.EventAddOnCatalogItemId,
            allocation.ReleasedAt,
        });
    }
}
