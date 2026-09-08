using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFinalizationEffectConfiguration : IEntityTypeConfiguration<RegistrationFinalizationEffect>
{
    public void Configure(EntityTypeBuilder<RegistrationFinalizationEffect> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_finalization_effects_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_registration_finalization_effects_processing_fence", "processing_fence >= 0");
            table.HasCheckConstraint(
                "ck_registration_finalization_effects_state",
                $"(status IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Failed}) AND " +
                "processing_lease_owner IS NULL AND processing_lease_token IS NULL AND processing_lease_expires_at IS NULL AND completed_at IS NULL) OR " +
                $"(status = {(int)OutboxMessageStatus.Processing} AND processing_lease_owner IS NOT NULL AND " +
                "processing_lease_token IS NOT NULL AND processing_lease_expires_at IS NOT NULL AND completed_at IS NULL) OR " +
                $"(status = {(int)OutboxMessageStatus.Completed} AND processing_lease_owner IS NULL AND " +
                "processing_lease_token IS NULL AND processing_lease_expires_at IS NULL AND completed_at IS NOT NULL)");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Status).IsRequired();
        builder.Property(value => value.ProcessingLeaseOwner).HasMaxLength(RegistrationFinalizationEffect.MaxLeaseOwnerLength);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId, value.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.EventId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.RegistrationOrderId })
            .IsUnique();
        builder.HasIndex(value => new { value.Status, value.NextAttemptAt, value.CreatedAt });
    }
}
