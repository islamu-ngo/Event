using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AdmissionTicketConfiguration : IEntityTypeConfiguration<AdmissionTicket>
{
    public void Configure(EntityTypeBuilder<AdmissionTicket> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_admission_tickets_transfer_hops",
                "transfer_hop_count >= 0");
        });
        builder.Property(ticket => ticket.Id).ValueGeneratedNever();
        builder.Property(ticket => ticket.DisplayReference).HasMaxLength(100).IsRequired();
        builder.Property(ticket => ticket.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(ticket => ticket.CreatedAt).IsRequired();
        builder.Ignore(ticket => ticket.CredentialGeneration);
        builder.Ignore(ticket => ticket.IsActive);
        builder.HasAlternateKey(ticket => new { ticket.TenantId, ticket.Id });
        builder.HasIndex(ticket => new { ticket.TenantId, ticket.RegistrationTicketAssignmentId })
            .IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(ticket => ticket.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(ticket => new { ticket.TenantId, ticket.EventId, ticket.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.EventId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrderLine>().WithMany()
            .HasForeignKey(ticket => new { ticket.TenantId, ticket.RegistrationOrderId, ticket.RegistrationOrderLineId })
            .HasPrincipalKey(line => new { line.TenantId, line.RegistrationOrderId, line.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationTicketAssignment>().WithMany()
            .HasForeignKey(ticket => new
            {
                ticket.TenantId,
                ticket.RegistrationOrderId,
                ticket.RegistrationTicketAssignmentId,
                ticket.RegistrationOrderLineId
            })
            .HasPrincipalKey(assignment => new
            {
                assignment.TenantId,
                assignment.RegistrationOrderId,
                assignment.Id,
                assignment.RegistrationOrderLineId
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationParticipant>().WithMany()
            .HasForeignKey(ticket => new { ticket.TenantId, ticket.RegistrationOrderId, ticket.ParticipantId })
            .HasPrincipalKey(participant => new { participant.TenantId, participant.RegistrationOrderId, participant.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketCatalogVersion>().WithMany()
            .HasForeignKey(ticket => new { ticket.TenantId, ticket.TicketCatalogVersionId })
            .HasPrincipalKey(catalog => new { catalog.TenantId, catalog.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventTicketType>().WithMany()
            .HasForeignKey(ticket => new { ticket.TenantId, ticket.EventTicketTypeId })
            .HasPrincipalKey(ticketType => new { ticketType.TenantId, ticketType.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(ticket => ticket.AdmissionTicketStatus).WithMany()
            .HasForeignKey(ticket => ticket.AdmissionTicketStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(ticket => ticket.LastTransitionReason).WithMany()
            .HasForeignKey(ticket => ticket.LastTransitionReasonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(ticket => ticket.Credentials).WithOne()
            .HasForeignKey(credential => new { credential.TenantId, credential.AdmissionTicketId })
            .HasPrincipalKey(ticket => new { ticket.TenantId, ticket.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdmissionTicketCredentialConfiguration : IEntityTypeConfiguration<AdmissionTicketCredential>
{
    public void Configure(EntityTypeBuilder<AdmissionTicketCredential> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_admission_ticket_credentials_versions", "credential_version > 0 AND lookup_key_version > 0");
        });
        builder.Property(credential => credential.Id).ValueGeneratedNever();
        builder.Property(credential => credential.LookupDigest).HasMaxLength(44).IsFixedLength().IsRequired();
        builder.Property(credential => credential.CreatedAt).IsRequired();
        builder.Property<int>("ActiveUniquenessSlot")
            .HasComputedColumnSql("CASE WHEN admission_ticket_credential_status_id = 1 THEN 0 ELSE credential_version END", stored: true);
        builder.HasAlternateKey(credential => new { credential.TenantId, credential.Id });
        builder.HasIndex(credential => new { credential.TenantId, credential.AdmissionTicketId, credential.CredentialVersion })
            .IsUnique();
        builder.HasIndex("TenantId", "AdmissionTicketId", "ActiveUniquenessSlot")
            .IsUnique();
        builder.HasIndex(credential => new { credential.TenantId, credential.LookupKeyVersion, credential.LookupDigest })
            .IsUnique();
        builder.HasOne(credential => credential.AdmissionTicketCredentialStatus).WithMany()
            .HasForeignKey(credential => credential.AdmissionTicketCredentialStatusId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdmissionDeliveryIntentConfiguration : IEntityTypeConfiguration<AdmissionDeliveryIntent>
{
    public void Configure(EntityTypeBuilder<AdmissionDeliveryIntent> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_admission_delivery_intents_protection_version", "protection_version > 0");
            table.HasCheckConstraint(
                "ck_admission_delivery_intents_handoff_receipt",
                "(handoff_completed_at IS NULL AND handoff_receipt_id IS NULL) OR (handoff_completed_at IS NOT NULL AND handoff_receipt_id IS NOT NULL)");
        });
        builder.Property(intent => intent.Id).ValueGeneratedNever();
        builder.Property(intent => intent.ProtectedCredential).HasMaxLength(2048).IsRequired();
        builder.Property(intent => intent.HandoffReceiptId).HasMaxLength(200);
        builder.Property(intent => intent.CreatedAt).IsRequired();
        builder.Property(intent => intent.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(intent => new { intent.TenantId, intent.Id });
        builder.HasIndex(intent => new { intent.TenantId, intent.FinalizationEffectId, intent.RegistrationTicketAssignmentId })
            .IsUnique();
        builder.HasIndex(intent => new { intent.HandoffCompletedAt, intent.RoutedAt, intent.CreatedAt });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(intent => intent.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFinalizationEffect>().WithMany()
            .HasForeignKey(intent => new { intent.TenantId, intent.FinalizationEffectId })
            .HasPrincipalKey(effect => new { effect.TenantId, effect.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationTicketAssignment>().WithMany()
            .HasForeignKey(intent => new { intent.TenantId, intent.RegistrationTicketAssignmentId })
            .HasPrincipalKey(assignment => new { assignment.TenantId, assignment.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdmissionTicket>().WithMany()
            .HasForeignKey(intent => new { intent.TenantId, intent.AdmissionTicketId })
            .HasPrincipalKey(ticket => new { ticket.TenantId, ticket.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdmissionTicketStatusConfiguration : IEntityTypeConfiguration<AdmissionTicketStatus>
{
    public void Configure(EntityTypeBuilder<AdmissionTicketStatus> builder)
    {
        builder.Property(value => value.MasterCode).HasMaxLength(40).IsRequired();
        builder.Property(value => value.FullName).HasMaxLength(100).IsRequired();
        builder.Property(value => value.Description).HasMaxLength(500);
        builder.HasIndex(value => value.MasterCode).IsUnique();
    }
}

public sealed class AdmissionTicketCredentialStatusConfiguration : IEntityTypeConfiguration<AdmissionTicketCredentialStatus>
{
    public void Configure(EntityTypeBuilder<AdmissionTicketCredentialStatus> builder)
    {
        builder.Property(value => value.MasterCode).HasMaxLength(40).IsRequired();
        builder.Property(value => value.FullName).HasMaxLength(100).IsRequired();
        builder.Property(value => value.Description).HasMaxLength(500);
        builder.HasIndex(value => value.MasterCode).IsUnique();
    }
}

public sealed class AdmissionTicketTransitionReasonConfiguration : IEntityTypeConfiguration<AdmissionTicketTransitionReason>
{
    public void Configure(EntityTypeBuilder<AdmissionTicketTransitionReason> builder)
    {
        builder.Property(value => value.MasterCode).HasMaxLength(40).IsRequired();
        builder.Property(value => value.FullName).HasMaxLength(100).IsRequired();
        builder.Property(value => value.Description).HasMaxLength(500);
        builder.HasIndex(value => value.MasterCode).IsUnique();
    }
}
