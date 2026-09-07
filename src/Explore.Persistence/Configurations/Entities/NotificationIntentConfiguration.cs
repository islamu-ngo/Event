// ABOUTME: EF Core mappings for durable notification intent, local delivery, and delegation audit rows.
// ABOUTME: Enforces tenant scoping, safe-payload metadata bounds, and normalized routing foreign keys.

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class NotificationIntentConfiguration : IEntityTypeConfiguration<NotificationIntent>
{
    public void Configure(EntityTypeBuilder<NotificationIntent> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_notification_intents_email_policy_revision_nonnegative",
            "email_delivery_policy_revision >= 0"));
        builder.Property(e => e.EmailDeliveryPolicyRevision).HasDefaultValue(0L);
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.TemplateKey).IsRequired().HasMaxLength(160);
        builder.Property(e => e.DeduplicationKey).IsRequired().HasMaxLength(300);
        builder.Property(e => e.SafePayloadReference).HasMaxLength(500);
        builder.Property(e => e.SafePayloadHash).HasMaxLength(128);
        builder.Property(e => e.CorrelationId).HasMaxLength(200);

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });
        builder.HasAlternateKey(e => new { e.TenantId, e.Id, e.RecipientUserId });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OwnershipType)
            .WithMany()
            .HasForeignKey(e => e.OwnershipTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RecipientKind)
            .WithMany()
            .HasForeignKey(e => e.RecipientKindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Status)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RecipientTenantUser)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.RecipientUserId })
            .HasPrincipalKey(e => new { e.TenantId, e.UserId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FanoutOccurrence)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.FanoutOccurrenceId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .HasConstraintName("fk_notification_intents_fanout_occurrence_tenant")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany()
            .HasForeignKey(e => e.ReportId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReportDecision)
            .WithMany()
            .HasForeignKey(e => e.ReportDecisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.DeduplicationKey })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(e => new { e.TenantId, e.StatusId, e.CreatedAt });

        builder.HasIndex(e => new { e.TenantId, e.CategoryId, e.CreatedAt });

        builder.HasIndex(e => new { e.TenantId, e.OwnershipTypeId, e.CreatedAt });

        builder.HasIndex(e => new { e.TenantId, e.FanoutOccurrenceId, e.RecipientUserId })
            .IsUnique();
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.ProviderMessageId).HasMaxLength(500);
        builder.Property(e => e.ProviderStatus).HasMaxLength(100);
        builder.Property(e => e.FailureCategory).HasMaxLength(100);
        builder.Property(e => e.ConsentPurpose).HasMaxLength(100);
        builder.Property(e => e.PreferenceCategoryCode).HasMaxLength(100);
        builder.Property(e => e.DisclosureLevel).HasMaxLength(100).IsRequired();
        builder.Property(e => e.TemplateKey).HasMaxLength(160).IsRequired();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id, e.NotificationIntentId, e.ChannelId })
            .HasName("ak_notification_deliveries_tenant_id_intent_channel");

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.NotificationIntent)
            .WithMany(e => e.Deliveries)
            .HasForeignKey(e => new { e.TenantId, e.NotificationIntentId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DeliveryPolicy)
            .WithMany()
            .HasForeignKey(e => e.DeliveryPolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EmailDispatchOutbox)
            .WithMany()
            .HasForeignKey(e => new
            {
                e.TenantId,
                e.EmailDispatchOutboxId,
                e.NotificationIntentId,
                e.RecipientAddressSource
            })
            .HasPrincipalKey(e => new
            {
                e.TenantId,
                e.Id,
                e.NotificationIntentId,
                e.RecipientAddressSource
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Notification)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.NotificationId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_notification_deliveries_notification_tenant");

        builder.HasOne(e => e.Status)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.NotificationIntentId, e.ChannelId })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.StatusId, e.CreatedAt });

        builder.HasIndex(e => new { e.TenantId, e.EmailDispatchOutboxId })
            .IsUnique()
            .HasFilter("email_dispatch_outbox_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.NotificationId })
            .IsUnique()
            .HasFilter("notification_id IS NOT NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_notification_deliveries_channel_link",
            "NOT (email_dispatch_outbox_id IS NOT NULL AND notification_id IS NOT NULL) " +
            "AND (email_dispatch_outbox_id IS NULL OR (channel_id = 1 AND recipient_address_source IS NOT NULL)) " +
            "AND (notification_id IS NULL OR channel_id = 2) " +
            "AND (channel_id <> 2 OR recipient_address_source IS NULL) " +
            "AND (email_dispatch_outbox_id IS NOT NULL OR recipient_address_source IS NULL)"));
    }
}

public sealed class NotificationExternalDelegationConfiguration : IEntityTypeConfiguration<NotificationExternalDelegation>
{
    public void Configure(EntityTypeBuilder<NotificationExternalDelegation> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.TemplateKey).IsRequired().HasMaxLength(160);
        builder.Property(e => e.SafePayloadHash).HasMaxLength(128);
        builder.Property(e => e.ExternalProviderId).HasMaxLength(200);
        builder.Property(e => e.ExternalCorrelationId).HasMaxLength(200);
        builder.Property(e => e.ExternalDeliveryStatus).HasMaxLength(100);
        builder.Property(e => e.FailureCategory).HasMaxLength(100);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.NotificationIntent)
            .WithMany(e => e.ExternalDelegations)
            .HasForeignKey(e => new { e.TenantId, e.NotificationIntentId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_notification_external_delegations_tenant_intent");

        builder.HasOne(e => e.ProviderKind)
            .WithMany()
            .HasForeignKey(e => e.ProviderKindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.AccountAuthorityKind)
            .WithMany()
            .HasForeignKey(e => e.AccountAuthorityKindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Status)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RecipientKind)
            .WithMany()
            .HasForeignKey(e => e.RecipientKindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany()
            .HasForeignKey(e => e.ReportId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReportDecision)
            .WithMany()
            .HasForeignKey(e => e.ReportDecisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.NotificationIntentId });

        builder.HasIndex(e => new { e.TenantId, e.ProviderKindId, e.StatusId, e.CreatedAt });

        builder.HasIndex(e => new { e.TenantId, e.AccountAuthorityKindId, e.StatusId, e.CreatedAt });

        builder.HasIndex(e => new { e.TenantId, e.ExternalCorrelationId });
    }
}
