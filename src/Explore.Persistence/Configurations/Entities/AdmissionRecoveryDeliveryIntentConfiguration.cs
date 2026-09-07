using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AdmissionRecoveryDeliveryIntentConfiguration :
    IEntityTypeConfiguration<AdmissionRecoveryDeliveryIntent>
{
    public void Configure(EntityTypeBuilder<AdmissionRecoveryDeliveryIntent> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_admission_recovery_delivery_intents_versions",
                "capability_version > 0 AND protection_version > 0");
            table.HasCheckConstraint(
                "ck_admission_recovery_delivery_intents_handoff",
                "(handoff_completed_at IS NULL AND handoff_receipt_id IS NULL) OR " +
                "(handoff_completed_at IS NOT NULL AND handoff_receipt_id IS NOT NULL)");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Purpose).HasMaxLength(40).IsRequired();
        builder.Property(value => value.ProtectedMaterial).HasMaxLength(4096).IsRequired();
        builder.Property(value => value.HandoffReceiptId).HasMaxLength(200);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.RecoveryRequestId,
                value.AdmissionTicketId,
                value.Purpose,
                value.CapabilityVersion
            })
            .IsUnique();
        builder.HasIndex(value => new { value.HandoffCompletedAt, value.RoutedAt, value.CreatedAt });
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdmissionTicket>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.AdmissionTicketId })
            .HasPrincipalKey(ticket => new { ticket.TenantId, ticket.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
