using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EmailDispatchAttemptConfiguration : IEntityTypeConfiguration<EmailDispatchAttempt>
{
    public void Configure(EntityTypeBuilder<EmailDispatchAttempt> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.Transport).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Provider).HasMaxLength(100);
        builder.Property(e => e.Outcome).IsRequired();
        builder.Property(e => e.FailureCategory).HasMaxLength(100);
        builder.Property(e => e.SanitizedErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.ProviderMessageId).HasMaxLength(500);
        builder.Property(e => e.CorrelationId).HasMaxLength(200);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EmailDispatchOutbox)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EmailDispatchOutboxId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.EmailDispatchOutboxId, e.AttemptNumber })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.StartedAt });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_email_dispatch_attempts_provider_handoff_fence",
            "failure_category <> 'provider_handoff_started' OR " +
            "(outcome = 3 AND completed_at IS NULL AND provider_message_id IS NULL)"));
    }
}
