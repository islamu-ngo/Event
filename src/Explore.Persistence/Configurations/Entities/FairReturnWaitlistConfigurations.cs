using Explore.Domain;
using Explore.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class FairReturnSupplyPolicyConfiguration :
    IEntityTypeConfiguration<FairReturnSupplyPolicy>
{
    public void Configure(
        EntityTypeBuilder<FairReturnSupplyPolicy> builder)
    {
        builder.ToTable(
            table => table.HasCheckConstraint(
                "ck_fair_return_supply_policy_lifetime",
                "offer_lifetime_minutes BETWEEN 5 AND 43200"));
        ConfigureTenantEntity(builder);
        builder.Property(value => value.EventId)
            .IsRequired();
        builder.Property(value =>
                value.TicketCatalogVersionId)
            .IsRequired();
        builder.Property(value =>
                value.EventTicketTypeId)
            .IsRequired();
        builder.Property(value => value.IsEnabled)
            .IsRequired();
        builder.Property(value =>
                value.OfferLifetimeMinutes)
            .IsRequired();
        builder.Property(value =>
                value.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.EventId,
                value.TicketCatalogVersionId,
                value.EventTicketTypeId,
            })
            .IsUnique();
    }

    internal static void ConfigureTenantEntity<TEntity>(
        EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ITenantEntity
    {
        builder.HasKey("Id");
        builder.Property<Guid>("Id")
            .ValueGeneratedNever();
        builder.Property(value => value.TenantId)
            .IsRequired();
        builder.HasAlternateKey("TenantId", "Id");
        builder.HasIndex("TenantId");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FairReturnSupplyUnitConfiguration :
    IEntityTypeConfiguration<FairReturnSupplyUnit>
{
    public void Configure(
        EntityTypeBuilder<FairReturnSupplyUnit> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fair_return_supply_units_amount",
                    "gross_minor_units >= 0");
                table.HasCheckConstraint(
                    "ck_fair_return_supply_units_status",
                    "status_id BETWEEN 1 AND 3");
                table.HasCheckConstraint(
                    "ck_fair_return_supply_units_state",
                    "(status_id = 1 AND bound_at IS NULL AND withdrawn_at IS NULL) OR (status_id = 2 AND bound_at IS NOT NULL AND withdrawn_at IS NULL) OR (status_id = 3 AND withdrawn_at IS NOT NULL)");
            });
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value => value.CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.CommercialTermsDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.AdmissionEntitlementDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.EventId,
                value.EventTicketTypeId,
                value.TicketCatalogVersionId,
                value.PurchasePolicySnapshotId,
                value.CurrencyCode,
                value.CommercialTermsDigest,
                value.AdmissionEntitlementDigest,
                value.GrossMinorUnits,
                value.RefundFundingModeId,
                value.StatusId,
                value.CreatedAt,
                value.Id,
            });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.SellerRegistrationOrderLineId,
            })
            .IsUnique();
    }
}

public sealed class EventWaitlistEntryConfiguration :
    IEntityTypeConfiguration<EventWaitlistEntry>
{
    public void Configure(
        EntityTypeBuilder<EventWaitlistEntry> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_event_waitlist_entries_status",
                    "status_id BETWEEN 1 AND 4");
                table.HasCheckConstraint(
                    "ck_event_waitlist_entries_amount",
                    "gross_minor_units >= 0");
                table.HasCheckConstraint(
                    "ck_event_waitlist_entries_state",
                    "(status_id IN (1, 2) AND open_registration_order_line_id IS NOT NULL) OR (status_id IN (3, 4) AND open_registration_order_line_id IS NULL)");
            });
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value =>
                value.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(value => value.CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.CommercialTermsDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.AdmissionEntitlementDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.EventId,
                value.EventTicketTypeId,
                value.StatusId,
                value.Priority,
                value.EnqueuedAt,
                value.Id,
            });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.OpenRegistrationOrderLineId,
            })
            .IsUnique();
    }
}

public sealed class EventWaitlistOfferConfiguration :
    IEntityTypeConfiguration<EventWaitlistOffer>
{
    public void Configure(
        EntityTypeBuilder<EventWaitlistOffer> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_event_waitlist_offers_status",
                    "status_id BETWEEN 1 AND 3");
                table.HasCheckConstraint(
                    "ck_event_waitlist_offers_state",
                    "(status_id = 1 AND open_event_waitlist_entry_id IS NOT NULL AND finalized_at IS NULL AND expired_at IS NULL) OR (status_id = 2 AND open_event_waitlist_entry_id IS NULL AND finalized_at IS NULL AND expired_at IS NOT NULL) OR (status_id = 3 AND open_event_waitlist_entry_id IS NULL AND finalized_at IS NOT NULL AND expired_at IS NULL)");
            });
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value =>
                value.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.OpenEventWaitlistEntryId,
            })
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.ExpiresAt,
                value.StatusId,
            });
        builder.HasOne<EventWaitlistEntry>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.EventWaitlistEntryId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FairReturnSupplyUnit>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.FairReturnSupplyUnitId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FairReturnSourceBinding>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.FairReturnSourceBindingId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FairReturnSourceBindingConfiguration :
    IEntityTypeConfiguration<FairReturnSourceBinding>
{
    public void Configure(
        EntityTypeBuilder<FairReturnSourceBinding> builder)
    {
        builder.ToTable(
            table => table.HasCheckConstraint(
                "ck_fair_return_source_bindings_amount",
                "unit_amount_minor >= 0"));
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value => value.CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.CommercialTermsDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.AdmissionEntitlementDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.BuyerRegistrationOrderLineId,
            })
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.FairReturnSupplyUnitId,
            })
            .IsUnique();
        builder.HasOne<FairReturnSupplyUnit>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.FairReturnSupplyUnitId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WaitlistProviderObservationConfiguration :
    IEntityTypeConfiguration<WaitlistProviderObservation>
{
    public void Configure(
        EntityTypeBuilder<WaitlistProviderObservation> builder)
    {
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value => value.ProviderCode)
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.ProviderObjectType)
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.ProviderObjectIdDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.ProviderObservationIdDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value => value.StateCode)
            .HasMaxLength(32)
            .IsUnicode(false)
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.ProviderCode,
                value.ProviderObjectType,
                value.ProviderObjectIdDigest,
            })
            .IsUnique();
        builder.HasOne<FairReturnSourceBinding>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.FairReturnSourceBindingId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WaitlistRefundIntentConfiguration :
    IEntityTypeConfiguration<WaitlistRefundIntent>
{
    public void Configure(
        EntityTypeBuilder<WaitlistRefundIntent> builder)
    {
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value =>
                value.ProviderIdempotencyKey)
            .HasMaxLength(200)
            .IsUnicode(false)
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.FairReturnSourceBindingId,
            })
            .IsUnique();
        builder.HasIndex(value => value.OutboxMessageId)
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.RefundAttemptId,
            })
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.StableOperationId,
            })
            .IsUnique();
        builder.HasOne<FairReturnSourceBinding>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.FairReturnSourceBindingId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OutboxMessage>()
            .WithMany()
            .HasForeignKey(value =>
                value.OutboxMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RefundAttempt>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.RefundAttemptId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WaitlistPaymentIntentConfiguration :
    IEntityTypeConfiguration<WaitlistPaymentIntent>
{
    public void Configure(
        EntityTypeBuilder<WaitlistPaymentIntent> builder)
    {
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value =>
                value.ProviderIdempotencyKey)
            .HasMaxLength(200)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(value =>
                value.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.FairReturnSourceBindingId,
            })
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.StableOperationId,
            })
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.ReservedRefundAttemptId,
            })
            .IsUnique();
        builder.HasOne<FairReturnSourceBinding>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.FairReturnSourceBindingId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.ReplacementPaymentAttemptId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RefundAttempt>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.ReservedRefundAttemptId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FairReturnOrchestrationEffectConfiguration :
    IEntityTypeConfiguration<FairReturnOrchestrationEffect>
{
    public void Configure(
        EntityTypeBuilder<FairReturnOrchestrationEffect> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_fair_return_effect_status",
                    "status_id BETWEEN 1 AND 4");
                table.HasCheckConstraint(
                    "ck_fair_return_effect_attempts",
                    "attempt_count >= 0 AND maximum_attempts BETWEEN 1 AND 100");
                table.HasCheckConstraint(
                    "ck_fair_return_effect_state",
                    "(status_id = 1 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NULL AND dead_lettered_at IS NULL) OR (status_id = 2 AND lease_expires_at IS NOT NULL AND lease_owner IS NOT NULL AND completed_at IS NULL AND dead_lettered_at IS NULL) OR (status_id = 3 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NOT NULL AND dead_lettered_at IS NULL) OR (status_id = 4 AND lease_expires_at IS NULL AND lease_owner IS NULL AND completed_at IS NULL AND dead_lettered_at IS NOT NULL)");
            });
        FairReturnSupplyPolicyConfiguration
            .ConfigureTenantEntity(builder);
        builder.Property(value => value.StableCursor)
            .ValueGeneratedNever();
        builder.Property(value => value.LeaseOwner)
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(value =>
                value.LastFailureCode)
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(value =>
                value.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(value => new
            {
                value.StableCursor,
                value.Id,
            });
        builder.HasIndex(value => new
            {
                value.StatusId,
                value.NextAttemptAt,
                value.CreatedAt,
                value.Id,
            });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.StableOperationId,
            })
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.WaitlistPaymentIntentId,
            })
            .IsUnique();
        builder.HasOne<WaitlistPaymentIntent>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.WaitlistPaymentIntentId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
