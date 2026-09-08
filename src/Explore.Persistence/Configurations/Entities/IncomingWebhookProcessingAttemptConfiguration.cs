using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class IncomingWebhookProcessingAttemptConfiguration : IEntityTypeConfiguration<IncomingWebhookProcessingAttempt>
{
    public void Configure(EntityTypeBuilder<IncomingWebhookProcessingAttempt> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_incoming_webhook_processing_attempts_generation",
                "processing_generation >= 1");
            table.HasCheckConstraint(
                "ck_incoming_webhook_processing_attempts_fence",
                "processing_fence >= 0");
            table.HasCheckConstraint(
                "ck_incoming_webhook_processing_attempts_number",
                "attempt_number >= 0");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.OutcomeId).IsRequired();
        builder.Ignore(e => e.Outcome);
        builder.Property(e => e.FailureCategory).HasMaxLength(IncomingWebhookMessage.MaxFailureCodeLength);
        builder.Property(e => e.SafeDetail).HasMaxLength(IncomingWebhookMessage.MaxSafeDetailLength);

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OutcomeLookup)
            .WithMany()
            .HasForeignKey(e => e.OutcomeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new
        {
            e.TenantId,
            e.IncomingWebhookMessageId,
            e.ProcessingGeneration,
            e.ProcessingFence,
            e.OutcomeId
        }).IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.RecordedAt });
    }
}
