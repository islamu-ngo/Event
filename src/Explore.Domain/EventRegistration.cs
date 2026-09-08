using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventRegistration : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public void ReassignParticipant(RegistrationParticipant participant, Guid concurrencyStamp)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (RegistrationOrderId != participant.RegistrationOrderId || TenantId != participant.TenantId || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("Admission participant must belong to the same order.", nameof(participant));
        }

        RegistrationParticipantId = participant.Id;
        RegistrationParticipant = participant;
        LinkedUserId = participant.LinkedUserId;
        ConcurrencyStamp = concurrencyStamp;
    }

    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    [ForeignKey("Event")]
    public Guid EventId { get; set; }
    public required Event Event { get; set; }

    [ForeignKey("LinkedUser")]
    public Guid? LinkedUserId { get; set; }
    public User? LinkedUser { get; set; }

    [ForeignKey("EventSession")]
    public Guid EventSessionId { get; set; }
    public required EventSession EventSession { get; set; }

    public DateTime CoverageEstablishedAt { get; set; }

    [ForeignKey("RegistrationOrder")]
    public Guid? RegistrationOrderId { get; set; }
    public RegistrationOrder? RegistrationOrder { get; set; }

    [ForeignKey("RegistrationOrderLine")]
    public Guid? RegistrationOrderLineId { get; set; }
    public RegistrationOrderLine? RegistrationOrderLine { get; set; }

    [ForeignKey("TicketTypeEntitlement")]
    public Guid? TicketTypeEntitlementId { get; set; }
    public TicketTypeEntitlement? TicketTypeEntitlement { get; set; }

    [ForeignKey("RegistrationParticipant")]
    public Guid RegistrationParticipantId { get; set; }
    public RegistrationParticipant RegistrationParticipant { get; set; } = null!;

    public int? EntitlementOrdinal { get; set; }

    [ForeignKey("ApprovalStatus")]
    public int? ApprovalStatusId { get; set; }
    public ApprovalStatus? ApprovalStatus { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    [ForeignKey("AtprotoRecord")]
    public Guid? AtprotoRecordId { get; set; }
    public AtprotoRecord? AtprotoRecord { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Soft delete fields
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
