using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventAddOnRefundAllocationConfiguration :
    IEntityTypeConfiguration<EventAddOnRefundAllocation>
{
    public void Configure(EntityTypeBuilder<EventAddOnRefundAllocation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_event_add_on_refund_allocations_quantity",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_event_add_on_refund_allocations_money",
                "amount_minor >= 0");
            table.HasCheckConstraint(
                "ck_event_add_on_refund_allocations_status",
                "status >= 1 AND status <= 4");
        });
        builder.Property(allocation => allocation.Id).ValueGeneratedNever();
        builder.Property(allocation => allocation.AmountMinor).HasColumnType("bigint");
        builder.Property(allocation => allocation.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(allocation => allocation.AllocatedAt).IsRequired();
        builder.Property(allocation => allocation.CreatedAt).IsRequired();
        builder.Property(allocation => allocation.Status)
            .HasConversion<int>()
            .HasDefaultValue(EventAddOnRefundAllocationStatus.PendingProvider)
            .IsRequired();
        builder.Property(allocation => allocation.ConcurrencyStamp).IsConcurrencyToken();
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
        builder.HasOne<RefundAttempt>()
            .WithMany()
            .HasForeignKey(allocation => new
            {
                allocation.TenantId,
                allocation.RefundOperationId,
            })
            .HasPrincipalKey(attempt => new
            {
                attempt.TenantId,
                attempt.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(allocation => new { allocation.TenantId, allocation.RefundOperationId })
            .IsUnique();
        builder.HasIndex(allocation => new
        {
            allocation.TenantId,
            allocation.RegistrationOrderAddOnLineId,
        });
    }
}
