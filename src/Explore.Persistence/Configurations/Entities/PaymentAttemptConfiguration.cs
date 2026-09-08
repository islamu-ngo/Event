using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_payment_attempts_status", "payment_attempt_status_id BETWEEN 1 AND 8");
            table.HasCheckConstraint("ck_payment_attempts_authoritative_status_floor", "authoritative_status_floor_id BETWEEN 1 AND 8");
            table.HasCheckConstraint(
                "ck_payment_attempts_amounts",
                "organizer_amount_minor >= 0 AND platform_fee_minor >= 0 AND platform_contribution_minor >= 0 AND total_minor >= 0 AND platform_fee_minor <= organizer_amount_minor AND total_minor = organizer_amount_minor + platform_contribution_minor");
            table.HasCheckConstraint("ck_payment_attempts_active_slot", $"(payment_attempt_status_id IN ({(int)PaymentAttemptStatusEnum.Failed}, {(int)PaymentAttemptStatusEnum.Cancelled}) AND active_uniqueness_slot <> '{PaymentAttempt.ActiveUniquenessSlotValue}') OR active_uniqueness_slot = '{PaymentAttempt.ActiveUniquenessSlotValue}'");
            table.HasCheckConstraint("ck_payment_attempts_campaign_cursor", "campaign_cursor > 0");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ProviderCode).IsRequired().HasMaxLength(40);
        builder.Property(value => value.ProfileCode).IsRequired().HasMaxLength(40);
        builder.Property(value => value.ProviderApiRevision).IsRequired().HasMaxLength(80);
        builder.Property(value => value.CompositionRevision).IsRequired().HasMaxLength(80);
        builder.Property(value => value.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(value => value.ProviderIdempotencyKey).IsRequired().HasMaxLength(160);
        builder.Property(value => value.ActiveScopeKey).IsRequired().HasMaxLength(170);
        builder.Property(value => value.ActiveUniquenessSlot).IsRequired().HasMaxLength(80);
        builder.Property(value => value.ProviderCheckoutSessionId).HasMaxLength(200);
        builder.Property(value => value.ProviderPaymentId).HasMaxLength(200);
        builder.Property(value => value.LastProviderRequestId).HasMaxLength(120);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.OwnsOne(value => value.RecipientSnapshot, owned =>
        {
            owned.Property(value => value.TenantId).HasColumnName("recipient_tenant_id");
            owned.Property(value => value.OrganizerActorId).HasColumnName("recipient_organizer_actor_id");
            owned.Property(value => value.OrganizerPaymentProviderConnectionId).HasColumnName("recipient_connection_id");
            owned.Property(value => value.ProviderCode).HasColumnName("recipient_provider_code").HasMaxLength(40).IsRequired();
            owned.Property(value => value.ConnectPlatformId).HasColumnName("recipient_connect_platform_id").HasMaxLength(120).IsRequired();
            owned.Property(value => value.ExternalAccountId).HasColumnName("recipient_external_account_id").HasMaxLength(200).IsRequired();
            owned.Property(value => value.MerchantCountryCode).HasColumnName("recipient_merchant_country_code").HasMaxLength(2).IsRequired();
            owned.Property(value => value.CurrencyCode).HasColumnName("recipient_currency_code").HasMaxLength(3).IsRequired();
            owned.Property(value => value.ProfileCode).HasColumnName("recipient_profile_code").HasMaxLength(40).IsRequired();
            owned.Property(value => value.InstancePolicyVersionId).HasColumnName("recipient_instance_policy_version_id");
            owned.Property(value => value.TenantPolicyVersionId).HasColumnName("recipient_tenant_policy_version_id");
            owned.Property(value => value.SnapshottedAt).HasColumnName("recipient_snapshotted_at");
        });
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.AcceptanceSnapshot).WithMany()
            .HasForeignKey(value => new { value.TenantId, AcceptanceId = value.PaidOrderAcceptanceSnapshotId })
            .HasPrincipalKey(value => new { value.TenantId, AcceptanceId = value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        builder.HasOne(value => value.PaymentAttemptStatus).WithMany().HasForeignKey(value => value.PaymentAttemptStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttemptStatus>().WithMany().HasForeignKey(value => value.AuthoritativeStatusFloorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ActiveScopeKey, value.ActiveUniquenessSlot }).IsUnique();
        builder.HasIndex(value => value.ProviderIdempotencyKey).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.RegistrationOrderId, value.PaymentAttemptStatusId });
        builder.HasIndex(value => new { value.TenantId, value.CampaignCursor });
    }
}

public sealed class PaymentAttemptStatusConfiguration : IEntityTypeConfiguration<PaymentAttemptStatus>
{
    public void Configure(EntityTypeBuilder<PaymentAttemptStatus> builder)
    {
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(value => value.FullName).IsRequired().HasMaxLength(200);
        builder.Property(value => value.Description).HasMaxLength(500);
        builder.HasIndex(value => value.MasterCode).IsUnique();
    }
}

public sealed class CheckoutDispatchEffectConfiguration : IEntityTypeConfiguration<CheckoutDispatchEffect>
{
    public void Configure(EntityTypeBuilder<CheckoutDispatchEffect> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_checkout_dispatch_effects_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_checkout_dispatch_effects_processing_fence", "processing_fence >= 0");
            table.HasCheckConstraint(
                "ck_checkout_dispatch_effects_state",
                $"(status IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Failed}) AND processing_lease_owner IS NULL AND processing_lease_token IS NULL AND processing_lease_expires_at IS NULL AND completed_at IS NULL AND parked_at IS NULL) OR " +
                $"(status = {(int)OutboxMessageStatus.Processing} AND processing_lease_owner IS NOT NULL AND processing_lease_token IS NOT NULL AND processing_lease_expires_at IS NOT NULL AND completed_at IS NULL AND parked_at IS NULL) OR " +
                $"(status = {(int)OutboxMessageStatus.Completed} AND processing_lease_owner IS NULL AND processing_lease_token IS NULL AND processing_lease_expires_at IS NULL AND completed_at IS NOT NULL AND parked_at IS NULL) OR " +
                $"(status = {(int)OutboxMessageStatus.DeadLettered} AND processing_lease_owner IS NULL AND processing_lease_token IS NULL AND processing_lease_expires_at IS NULL AND completed_at IS NULL AND parked_at IS NOT NULL) OR " +
                $"(status = {(int)OutboxMessageStatus.Unknown} AND processing_lease_owner IS NULL AND processing_lease_token IS NULL AND processing_lease_expires_at IS NULL AND completed_at IS NULL AND parked_at IS NULL AND unknown_at IS NOT NULL)");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Status).IsRequired();
        builder.Property(value => value.ProcessingLeaseOwner).HasMaxLength(CheckoutDispatchEffect.MaxLeaseOwnerLength);
        builder.Property(value => value.LastFailureCode).HasMaxLength(CheckoutDispatchEffect.MaxFailureCodeLength);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaymentAttemptId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.PaymentAttemptId }).IsUnique();
        builder.HasIndex(value => new { value.Status, value.NextAttemptAt, value.CreatedAt });
    }
}

public sealed class PaymentReconciliationEffectConfiguration : IEntityTypeConfiguration<PaymentReconciliationEffect>
{
    public void Configure(EntityTypeBuilder<PaymentReconciliationEffect> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_payment_reconciliation_effects_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_payment_reconciliation_effects_processing_fence", "processing_fence >= 0");
            table.HasCheckConstraint(
                "ck_payment_reconciliation_effects_dispatch_unknown_epoch",
                "(checkout_dispatch_effect_id IS NULL AND checkout_dispatch_unknown_at IS NULL AND checkout_dispatch_processing_fence IS NULL AND checkout_dispatch_attempt_count IS NULL) OR " +
                "(checkout_dispatch_effect_id IS NOT NULL AND checkout_dispatch_unknown_at IS NOT NULL AND checkout_dispatch_processing_fence >= 0 AND checkout_dispatch_attempt_count >= 0)");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Status).IsRequired();
        builder.Property(value => value.ProcessingFence).IsConcurrencyToken();
        builder.Property(value => value.ProcessingLeaseOwner).HasMaxLength(PaymentReconciliationEffect.MaxLeaseOwnerLength);
        builder.Property(value => value.LastFailureCode).HasMaxLength(PaymentReconciliationEffect.MaxFailureCodeLength);
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaymentAttemptId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IncomingWebhookMessage>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.SourceIncomingWebhookMessageId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CheckoutDispatchEffect>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.CheckoutDispatchEffectId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.PaymentAttemptId }).IsUnique();
        builder.HasIndex(value => new { value.Status, value.NextAttemptAt, value.CreatedAt, value.Id });
        builder.HasIndex(value => new { value.Status, value.ProcessingLeaseExpiresAt, value.CreatedAt, value.Id });
    }
}

public sealed class PaymentSucceededObservationConfiguration : IEntityTypeConfiguration<PaymentSucceededObservation>
{
    public void Configure(EntityTypeBuilder<PaymentSucceededObservation> builder)
    {
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ProviderCheckoutSessionId).HasMaxLength(200).IsRequired();
        builder.Property(value => value.ProviderPaymentId).HasMaxLength(200).IsRequired();
        builder.Property(value => value.ProviderRequestId).HasMaxLength(120);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaymentAttemptId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IncomingWebhookMessage>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.SourceIncomingWebhookMessageId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.PaymentAttemptId }).IsUnique();
    }
}
