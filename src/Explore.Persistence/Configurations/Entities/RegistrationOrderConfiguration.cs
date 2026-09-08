// ABOUTME: EF configuration for the tenant-scoped registration order aggregate and immutable snapshots.
// ABOUTME: Maps restrictive tenant-safe relationships so order history cannot be cascade-deleted.

using Explore.Domain;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationOrderConfiguration : IEntityTypeConfiguration<RegistrationOrder>
{
    public void Configure(EntityTypeBuilder<RegistrationOrder> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_registration_orders_workflow_key",
            "(registration_workflow_version_id IS NULL AND registration_workflow_version_key = '00000000-0000-0000-0000-000000000000') OR " +
            "registration_workflow_version_key = registration_workflow_version_id"));
        builder.Property(order => order.Id).ValueGeneratedNever();
        var workflowVersionKey = builder.Property<Guid>("RegistrationWorkflowVersionKey");
        workflowVersionKey
            .HasColumnType("uuid")
            .HasComputedColumnSql(
                "COALESCE(registration_workflow_version_id, '00000000-0000-0000-0000-000000000000'::uuid)",
                stored: true)
            .ValueGeneratedOnAdd()
            .IsRequired();
        workflowVersionKey.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        workflowVersionKey.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(order => order.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(order => order.GuestAccessTokenHash)
            .HasConversion(hash => hash!.Value, value => CapabilityTokenHash.Create(value))
            .HasMaxLength(44);
        builder.Property(order => order.GuestStatusAccessUntilUtc).HasConversion(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null);
        var anonymousRetention = builder.Property(order => order.AnonymousPiiRetentionUntilUtc).HasConversion(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null);
        anonymousRetention.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(order => order.AppliedPromotionDisplayLabelSnapshot).HasMaxLength(16);
        builder.Property(order => order.PreDiscountOrganizerDirectedTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.PromotionDiscountTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.PostDiscountOrganizerDirectedTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.OrganizerDirectedTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.PlatformFeeTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.OrganizerEarningsTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.PlatformContributionTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.TotalDueMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.AddOnTotalMinorSnapshot).HasColumnType("bigint");
        builder.Property(order => order.CreatedAt).IsRequired();
        builder.Property(order => order.IsDeleted).HasDefaultValue(false);
        builder.Property(order => order.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(order => new { order.TenantId, order.Id });
        builder.HasAlternateKey(order => new { order.TenantId, order.EventId, order.Id });
        builder.HasAlternateKey(
            nameof(RegistrationOrder.TenantId),
            nameof(RegistrationOrder.EventId),
            "RegistrationWorkflowVersionKey",
            nameof(RegistrationOrder.Id));
        builder.HasMany<RegistrationAttempt>().WithOne()
            .HasForeignKey(
                nameof(RegistrationAttempt.TenantId),
                nameof(RegistrationAttempt.EventId),
                nameof(RegistrationAttempt.RegistrationWorkflowId),
                nameof(RegistrationAttempt.RegistrationOrderId))
            .HasPrincipalKey(
                nameof(RegistrationOrder.TenantId),
                nameof(RegistrationOrder.EventId),
                "RegistrationWorkflowVersionKey",
                nameof(RegistrationOrder.Id))
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(order => order.ParticipationSnapshot, snapshot =>
        {
            snapshot.Property(value => value.ConfigurationVersion).HasColumnName("participation_configuration_version_snapshot");
            snapshot.Property(value => value.ParticipationHandlingModeId).HasColumnName("participation_handling_mode_id_snapshot");
            snapshot.Property(value => value.AdvanceRegistrationObligationId).HasColumnName("advance_registration_obligation_id_snapshot");
            snapshot.Property(value => value.IdentityAccessModeId).HasColumnName("identity_access_mode_id_snapshot");
            snapshot.Property(value => value.GuestRecoveryPolicy).HasColumnName("guest_recovery_policy_snapshot");
        });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(order => order.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(order => new { order.TenantId, order.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketCatalogVersion>().WithMany().HasForeignKey(order => new { order.TenantId, order.TicketCatalogVersionId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventAddOnCatalogVersion>().WithMany()
            .HasForeignKey(order => new
            {
                order.TenantId,
                order.EventId,
                order.AddOnCatalogVersionIdSnapshot,
            })
            .HasPrincipalKey(catalog => new
            {
                catalog.TenantId,
                catalog.EventId,
                catalog.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromotionDefinition>().WithMany().HasForeignKey(order => new { order.TenantId, order.AppliedPromotionDefinitionVersionIdSnapshot })
            .HasPrincipalKey(definition => new { definition.TenantId, definition.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromotionCode>().WithMany().HasForeignKey(order => new { order.TenantId, order.AppliedPromotionCodeIdSnapshot })
            .HasPrincipalKey(code => new { code.TenantId, code.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.BookingPartyType).WithMany().HasForeignKey(order => order.BookingPartyTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.RegistrationOrderStatus).WithMany().HasForeignKey(order => order.RegistrationOrderStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Lines).WithOne().HasForeignKey(line => new { line.TenantId, line.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.AddOnLines).WithOne()
            .HasForeignKey(line => new { line.TenantId, line.EventId, line.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.EventId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Participants).WithOne(participant => participant.RegistrationOrder)
            .HasForeignKey(participant => new { participant.TenantId, participant.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.Pii).WithOne(pii => pii.RegistrationOrder).HasForeignKey<RegistrationOrderPii>(pii => new { pii.TenantId, pii.RegistrationOrderId })
            .HasPrincipalKey<RegistrationOrder>(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.PlatformContribution).WithOne().HasForeignKey<RegistrationOrderPlatformContribution>(contribution => new { contribution.TenantId, contribution.RegistrationOrderId })
            .HasPrincipalKey<RegistrationOrder>(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(order => new { order.TenantId, order.EventId, order.RegistrationOrderStatusId });
        builder.HasIndex(order => new { order.TenantId, order.ExpiresAt });
    }
}
