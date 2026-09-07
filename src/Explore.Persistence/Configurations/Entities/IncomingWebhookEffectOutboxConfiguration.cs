using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class IncomingWebhookEffectOutboxConfiguration : IEntityTypeConfiguration<IncomingWebhookEffectOutbox>
{
    public void Configure(EntityTypeBuilder<IncomingWebhookEffectOutbox> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_incoming_webhook_effect_outbox_payload_sha256",
                "payload_sha256 ~ '^sha256:[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_incoming_webhook_effect_outbox_processing_generation",
                "processing_generation >= 1");
            table.HasCheckConstraint(
                "ck_incoming_webhook_effect_outbox_processing_fence",
                "processing_fence >= 0");
            table.HasCheckConstraint(
                "ck_incoming_webhook_effect_outbox_attempt_count",
                "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_incoming_webhook_effect_outbox_failure_category",
                "failure_category IS NULL OR failure_category ~ '^[a-z0-9_]+$'");
        });

        builder.Property(pointer => pointer.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(pointer => pointer.Provider)
            .HasMaxLength(IncomingWebhookMessage.MaxProviderLength)
            .IsRequired();
        builder.Property(pointer => pointer.ProviderDecisionId)
            .HasMaxLength(IncomingWebhookEffectOutbox.MaxProviderDecisionIdLength)
            .IsRequired();
        builder.Property(pointer => pointer.EffectKind)
            .HasMaxLength(IncomingWebhookEffectReceipt.MaxEffectKindLength)
            .IsRequired();
        builder.Property(pointer => pointer.PayloadSha256).HasMaxLength(71).IsRequired();
        builder.Property(pointer => pointer.Status).IsRequired();
        builder.Property(pointer => pointer.ProcessingGeneration).HasDefaultValue(1).IsRequired();
        builder.Property(pointer => pointer.ProcessingLeaseOwner)
            .HasMaxLength(IncomingWebhookEffectOutbox.MaxLeaseOwnerLength);
        builder.Property(pointer => pointer.FailureCategory)
            .HasMaxLength(IncomingWebhookEffectOutbox.MaxFailureCategoryLength);
        builder.Property(pointer => pointer.SafeDetail)
            .HasMaxLength(IncomingWebhookEffectOutbox.MaxSafeDetailLength);

        builder.HasAlternateKey(pointer => new { pointer.TenantId, pointer.Id });

        builder.HasOne(pointer => pointer.Tenant)
            .WithMany()
            .HasForeignKey(pointer => pointer.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pointer => pointer.IncomingWebhookMessage)
            .WithMany()
            .HasForeignKey(pointer => new { pointer.TenantId, pointer.IncomingWebhookMessageId })
            .HasPrincipalKey(message => new { message.TenantId, message.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pointer => new
        {
            pointer.TenantId,
            pointer.Provider,
            pointer.ProviderDecisionId,
            pointer.EffectKind
        }).IsUnique();

        builder.HasIndex(pointer => new
        {
            pointer.TenantId,
            pointer.IncomingWebhookMessageId,
            pointer.EffectKind
        }).IsUnique();

        builder.HasIndex(pointer => new { pointer.Status, pointer.NextAttemptAt, pointer.CreatedAt });
    }
}
