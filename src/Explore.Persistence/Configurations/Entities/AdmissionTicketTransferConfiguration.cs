using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class TicketTransferPolicyConfiguration :
    IEntityTypeConfiguration<TicketTransferPolicy>
{
    public void Configure(
        EntityTypeBuilder<TicketTransferPolicy> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_ticket_transfer_policies_bounds",
                "maximum_hops BETWEEN 1 AND 100 AND offer_lifetime_minutes BETWEEN 5 AND 43200 AND cutoff_minutes_before_event BETWEEN 0 AND 525600");
        });
        builder.Property(value => value.Id)
            .ValueGeneratedNever();
        builder.Property(value => value.CreatedAt)
            .IsRequired();
        builder.Property(value => value.ConcurrencyStamp)
            .IsConcurrencyToken();
        builder.HasAlternateKey(value => new
        {
            value.TenantId,
            value.Id,
        });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.EventTicketTypeId,
            })
            .HasDatabaseName(
                "ux_ticket_transfer_policies_ticket_type")
            .IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketCatalogVersion>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.TicketCatalogVersionId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketType>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.EventTicketTypeId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdmissionTicketTransferConfiguration :
    IEntityTypeConfiguration<AdmissionTicketTransfer>
{
    public void Configure(
        EntityTypeBuilder<AdmissionTicketTransfer> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_admission_ticket_transfers_positive",
                "transfer_hop > 0 AND credential_generation > 0 AND (accepted_credential_generation IS NULL OR accepted_credential_generation = credential_generation + 1)");
            table.HasCheckConstraint(
                "ck_admission_ticket_transfers_status",
                "status_id BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_admission_ticket_transfers_terminal_facts",
                "(status_id = 1 AND accepted_at IS NULL AND cancelled_at IS NULL AND expired_at IS NULL AND capability_consumed_at IS NULL AND accepted_credential_generation IS NULL AND to_participant_id IS NULL AND recipient_subject_user_id IS NULL) OR (status_id = 2 AND accepted_at IS NOT NULL AND capability_consumed_at IS NOT NULL AND accepted_credential_generation IS NOT NULL AND to_participant_id IS NOT NULL AND recipient_subject_user_id IS NOT NULL AND cancelled_at IS NULL AND expired_at IS NULL) OR (status_id = 3 AND cancelled_at IS NOT NULL AND accepted_at IS NULL AND expired_at IS NULL) OR (status_id = 4 AND expired_at IS NOT NULL AND accepted_at IS NULL AND cancelled_at IS NULL)");
        });
        builder.Property(value => value.Id)
            .ValueGeneratedNever();
        builder.Property(value => value.CapabilityDigest)
            .HasMaxLength(44)
            .IsFixedLength()
            .IsRequired();
        builder.Property(value => value.CreatedAt)
            .IsRequired();
        builder.Property(value => value.ConcurrencyStamp)
            .IsConcurrencyToken();
        builder.Ignore(value => value.IsOpen);
        builder.HasAlternateKey(value => new
        {
            value.TenantId,
            value.Id,
        });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.OpenAdmissionTicketId,
            })
            .HasDatabaseName(
                "ux_admission_ticket_transfers_open")
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.OfferOperationKey,
            })
            .HasDatabaseName(
                "ux_admission_ticket_transfers_operation")
            .IsUnique();
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.CapabilityDigest,
            })
            .HasDatabaseName(
                "ux_admission_ticket_transfers_capability")
            .IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdmissionTicket>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.AdmissionTicketId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationTicketAssignment>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.RegistrationTicketAssignmentId,
                value.RegistrationOrderLineId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.Id,
                value.RegistrationOrderLineId,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationParticipant>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.FromParticipantId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationParticipant>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.ToParticipantId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(value =>
                value.RecipientSubjectUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdmissionTransferDeliveryIntentConfiguration :
    IEntityTypeConfiguration<AdmissionTransferDeliveryIntent>
{
    public void Configure(
        EntityTypeBuilder<AdmissionTransferDeliveryIntent> builder)
    {
        builder.Property(value => value.Id)
            .ValueGeneratedNever();
        builder.Property(value => value.CreatedAt)
            .IsRequired();
        builder.HasAlternateKey(value => new
        {
            value.TenantId,
            value.Id,
        });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.AdmissionTicketTransferId,
            })
            .HasDatabaseName(
                "ux_admission_transfer_delivery_intents_transfer")
            .IsUnique();
        builder.HasIndex(value => value.OutboxMessageId)
            .HasDatabaseName(
                "ux_admission_transfer_delivery_intents_outbox")
            .IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdmissionTicketTransfer>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.AdmissionTicketTransferId,
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OutboxMessage>()
            .WithMany()
            .HasForeignKey(value => value.OutboxMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
