using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationProviderSubmissionWriteEffectConfiguration : IEntityTypeConfiguration<RegistrationProviderSubmissionWriteEffect>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderSubmissionWriteEffect> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_provider_submission_write_effects_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_registration_provider_submission_write_effects_processing_fence", "processing_fence >= 0");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Status).IsRequired();
        builder.Property(value => value.ProcessingLeaseOwner).HasMaxLength(RegistrationProviderSubmissionWriteEffect.MaxLeaseOwnerLength);
        builder.Property(value => value.FailureCode).HasMaxLength(RegistrationProviderSubmissionWriteEffect.MaxFailureCodeLength);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId, value.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.EventId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.RegistrationSubmissionId })
            .IsUnique();
        builder.HasIndex(value => new { value.Status, value.NextAttemptAt, value.CreatedAt });
    }
}
