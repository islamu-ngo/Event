using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventRoleAssignment : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }

    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    [ForeignKey(nameof(Event))]
    public Guid EventId { get; set; }
    public required Event Event { get; set; }

    [ForeignKey(nameof(User))]
    public Guid UserId { get; set; }
    public required User User { get; set; }

    [ForeignKey(nameof(Role))]
    public int RoleId { get; set; }
    public required Role Role { get; set; }

    public EventRoleAssignmentStatus Status { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }

    /// <summary>
    /// Application-managed optimistic concurrency token for PostgreSQL.
    /// Incremented by domain lifecycle transitions instead of using SQL Server rowversion semantics.
    /// </summary>
    public long Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventRoleAssignment Create(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        int roleId,
        EventRoleAssignmentStatus status,
        DateTime startsAtUtc,
        DateTime? expiresAtUtc,
        Guid createdByUserId)
    {
        ValidateValidityWindow(startsAtUtc, expiresAtUtc);

        if (status is EventRoleAssignmentStatus.Revoked or EventRoleAssignmentStatus.Expired)
        {
            throw new ArgumentException("New event role assignments must start as Pending or Active.", nameof(status));
        }

        return new EventRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            UserId = userId,
            RoleId = roleId,
            Status = status,
            StartsAtUtc = startsAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            CreatedBy = createdByUserId,
            Version = 1,
            Tenant = null!,
            Event = null!,
            User = null!,
            Role = null!
        };
    }

    public bool IsEffectiveAt(DateTime utcNow)
    {
        return Status == EventRoleAssignmentStatus.Active
            && StartsAtUtc <= utcNow
            && (ExpiresAtUtc is null || ExpiresAtUtc > utcNow);
    }

    public void Activate(DateTime utcNow)
    {
        EnsureStatus(EventRoleAssignmentStatus.Pending, "Only pending event role assignments can be activated.");

        Status = EventRoleAssignmentStatus.Active;
        Touch(utcNow);
    }

    public void Revoke(Guid actorUserId, DateTime utcNow)
    {
        if (Status is EventRoleAssignmentStatus.Revoked or EventRoleAssignmentStatus.Expired)
        {
            throw new InvalidOperationException("Terminal event role assignments cannot be revoked again.");
        }

        Status = EventRoleAssignmentStatus.Revoked;
        RevokedAtUtc = utcNow;
        RevokedByUserId = actorUserId;
        Touch(utcNow);
    }

    public void MarkExpired(DateTime utcNow)
    {
        EnsureStatus(EventRoleAssignmentStatus.Active, "Only active event role assignments can be materialized as expired.");

        if (ExpiresAtUtc is null || ExpiresAtUtc > utcNow)
        {
            throw new InvalidOperationException("Event role assignment has not reached its expiration time.");
        }

        Status = EventRoleAssignmentStatus.Expired;
        Touch(utcNow);
    }

    public void UpdateValidityWindow(DateTime startsAtUtc, DateTime? expiresAtUtc, DateTime utcNow)
    {
        if (Status is EventRoleAssignmentStatus.Revoked or EventRoleAssignmentStatus.Expired)
        {
            throw new InvalidOperationException("Terminal event role assignments cannot change validity windows.");
        }

        ValidateValidityWindow(startsAtUtc, expiresAtUtc);

        StartsAtUtc = startsAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Touch(utcNow);
    }

    private static void ValidateValidityWindow(DateTime startsAtUtc, DateTime? expiresAtUtc)
    {
        if (expiresAtUtc is not null && expiresAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Event role assignment expiration must be after its start time.", nameof(expiresAtUtc));
        }
    }

    private void EnsureStatus(EventRoleAssignmentStatus expectedStatus, string message)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedAt = utcNow;
        Version++;
    }
}
