using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class WebhookDeliveryPlanSnapshotConfiguration
    : IEntityTypeConfiguration<WebhookDeliveryPlanSnapshot>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryPlanSnapshot> builder)
    {
        builder.Property(plan => plan.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(plan => plan.ProviderModeId).IsRequired();
        builder.Property(plan => plan.ConfigurationVersion).HasMaxLength(WebhookDeliveryPlanSnapshot.MaxVersionLength).IsRequired();
        builder.Property(plan => plan.EventContractVersion).HasMaxLength(WebhookDeliveryPlanSnapshot.MaxVersionLength).IsRequired();
        builder.Property(plan => plan.RetentionPolicy).HasMaxLength(WebhookDeliveryPlanSnapshot.MaxRetentionPolicyLength).IsRequired();
        builder.Property(plan => plan.RetentionPolicyVersion).HasMaxLength(WebhookDeliveryPlanSnapshot.MaxVersionLength).IsRequired();
        builder.Property(plan => plan.AttemptRetentionUntilUtc)
            .HasDefaultValueSql("statement_timestamp() + INTERVAL '30 days'");
        builder.Property(plan => plan.DeadLetterEvidenceRetentionUntilUtc)
            .HasDefaultValueSql("statement_timestamp() + INTERVAL '90 days'");
        builder.Property(plan => plan.PublicationRetentionUntilUtc)
            .HasDefaultValueSql("statement_timestamp() + INTERVAL '90 days'");
        builder.Property(plan => plan.OperationalLogRetentionUntilUtc)
            .HasDefaultValueSql("statement_timestamp() + INTERVAL '30 days'");
        builder.Ignore(plan => plan.ProviderMode);

        builder.HasAlternateKey(plan => new { plan.TenantId, plan.Id });

        builder.HasOne(plan => plan.Tenant)
            .WithMany()
            .HasForeignKey(plan => plan.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(plan => plan.WebhookMessage)
            .WithMany()
            .HasPrincipalKey(message => new { message.TenantId, message.Id })
            .HasForeignKey(plan => new { plan.TenantId, plan.WebhookMessageId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(plan => plan.WebhookConsumer)
            .WithMany()
            .HasForeignKey(plan => plan.WebhookConsumerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(plan => plan.ProviderModeLookup)
            .WithMany()
            .HasForeignKey(plan => plan.ProviderModeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(plan => new { plan.TenantId, plan.WebhookMessageId })
            .IsUnique();
        builder.HasIndex(plan => new { plan.TenantId, plan.WebhookConsumerId, plan.MaterializedAtUtc });
        builder.HasIndex(plan => new { plan.TenantId, plan.PayloadRetentionUntilUtc });
        builder.HasIndex(plan => new { plan.TenantId, plan.AttemptRetentionUntilUtc });
        builder.HasIndex(plan => new { plan.TenantId, plan.DeadLetterEvidenceRetentionUntilUtc });
        builder.HasIndex(plan => new { plan.TenantId, plan.PublicationRetentionUntilUtc });
    }
}
