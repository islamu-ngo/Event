using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class IncomingWebhookEffectReceiptConfiguration : IEntityTypeConfiguration<IncomingWebhookEffectReceipt>
{
    public void Configure(EntityTypeBuilder<IncomingWebhookEffectReceipt> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_incoming_webhook_effect_receipts_payload_hash",
                "payload_hash ~ '^sha256:[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_incoming_webhook_effect_receipts_processing_generation",
                "processing_generation >= 1");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.EffectKind).HasMaxLength(IncomingWebhookEffectReceipt.MaxEffectKindLength).IsRequired();
        builder.Property(e => e.PayloadHash).HasMaxLength(71).IsRequired();
        builder.Property(e => e.ProcessingGeneration).IsRequired();
        builder.Property(e => e.SafeResultReference)
            .HasMaxLength(IncomingWebhookEffectReceipt.MaxSafeResultReferenceLength);

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.IncomingWebhookMessage)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.IncomingWebhookMessageId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.IncomingWebhookMessageId, e.EffectKind })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.AppliedAt });
    }
}
