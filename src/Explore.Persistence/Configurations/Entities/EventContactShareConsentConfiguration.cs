using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventContactShareConsentConfiguration : IEntityTypeConfiguration<EventContactShareConsent>
{
    public void Configure(EntityTypeBuilder<EventContactShareConsent> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_event_contact_share_consents_subject_shape",
            "(CASE WHEN user_subject_id IS NULL THEN 0 ELSE 1 END + " +
            "CASE WHEN registration_purchaser_order_id IS NULL THEN 0 ELSE 1 END + " +
            "CASE WHEN registration_participant_id IS NULL THEN 0 ELSE 1 END + " +
            "CASE WHEN guest_contact_order_id IS NULL THEN 0 ELSE 1 END) = 1"));
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmailSnapshot).IsRequired().HasMaxLength(320);
        builder.Property(e => e.EmailNormalizedSnapshot).IsRequired().HasMaxLength(320);
        builder.Property(e => e.PurposeCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ConsentTextSnapshot).IsRequired().HasMaxLength(4000);
        builder.Property(e => e.ConsentUiVersion).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.SubjectType).WithMany().HasForeignKey(e => e.SubjectTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.UserSubject).WithMany().HasForeignKey(e => e.UserSubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.RecipientActor).WithMany().HasForeignKey(e => e.RecipientActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.RegistrationPurchaserOrder).WithMany()
            .HasForeignKey(e => new { e.TenantId, e.RegistrationPurchaserOrderId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.GuestContactOrder).WithMany()
            .HasForeignKey(e => new { e.TenantId, e.GuestContactOrderId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.RegistrationParticipant).WithMany()
            .HasForeignKey(e => new { e.TenantId, e.RegistrationParticipantId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id }).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.SubjectTypeId, e.SubjectId, e.RecipientActorId, e.PurposeCode })
            .IsUnique().HasDatabaseName("ux_event_contact_share_consents_current_scope");
        builder.HasIndex(e => new { e.TenantId, e.RecipientActorId, e.Status })
            .HasDatabaseName("ix_event_contact_share_consents_recipient_status");
        builder.HasIndex(e => new { e.TenantId, e.SubjectTypeId, e.SubjectId, e.Status })
            .HasDatabaseName("ix_event_contact_share_consents_subject_status");
    }
}
