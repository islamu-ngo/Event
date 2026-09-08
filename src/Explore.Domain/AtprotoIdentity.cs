using System.Diagnostics.CodeAnalysis;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public class AtprotoIdentity : IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public string Did { get; private set; } = null!;
    public Guid ActorId { get; set; }
    public required Actor Actor { get; set; }
    public int? DidCustodyTypeId { get; set; }
    public DidCustodyType? DidCustodyType { get; set; }
    public string? Handle { get; set; }
    public required string PdsHost { get; set; }
    public string? SigningKey { get; set; }
    public bool IsActive { get; set; }
    public bool IsSuspended { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public Guid? SuspendedBy { get; set; }
    public string? ModerationReasonCode { get; set; }
    public DateTime LastResolvedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public ICollection<AtprotoIdentityModerationRecord> ModerationRecords { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    private AtprotoIdentity()
    {
    }

    [SetsRequiredMembers]
    public AtprotoIdentity(AtprotoDid did)
    {
        Did = did.Value;
        Actor = null!;
        PdsHost = string.Empty;
    }

    public void RefreshVerifiedMetadata(AtprotoDid did, string? handle, string pdsHost, string? signingKey, DateTime resolvedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdsHost);
        if (!string.Equals(Did, did.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Verified metadata cannot change the identity DID.");
        }

        Handle = string.IsNullOrWhiteSpace(handle) ? null : handle.Trim().ToLowerInvariant();
        PdsHost = pdsHost.Trim();
        SigningKey = string.IsNullOrWhiteSpace(signingKey) ? null : signingKey.Trim();
        LastResolvedAt = resolvedAt;
        LastSeenAt = resolvedAt;
        IsActive = true;
    }

    public void EraseForPrivacy(DateTime erasedAt)
    {
        if (erasedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A UTC erasure timestamp is required.", nameof(erasedAt));
        }

        Did = $"did:deleted:{Id:N}";
        Handle = null;
        PdsHost = string.Empty;
        SigningKey = null;
        IsActive = false;
        IsDeleted = true;
        CreatedBy = null;
        UpdatedBy = null;
        DeletedAt = erasedAt;
        DeletedBy = null;
    }

    public void Suspend(string reasonCode, DateTime when, Guid by)
    {
        string normalizedReasonCode = ValidateModerationTransition(reasonCode, when, by);
        if (IsSuspended)
        {
            return;
        }

        IsSuspended = true;
        SuspendedAt = when;
        SuspendedBy = by;
        ModerationReasonCode = normalizedReasonCode;
        ModerationRecords.Add(AtprotoIdentityModerationRecord.Create(
            Id,
            GlobalModerationAction.Suspend,
            normalizedReasonCode,
            when,
            by));
        MarkUpdated(when, by);
    }

    public void Reinstate(string reasonCode, DateTime when, Guid by)
    {
        string normalizedReasonCode = ValidateModerationTransition(reasonCode, when, by);
        if (!IsSuspended)
        {
            return;
        }

        IsSuspended = false;
        SuspendedAt = null;
        SuspendedBy = null;
        ModerationReasonCode = null;
        ModerationRecords.Add(AtprotoIdentityModerationRecord.Create(
            Id,
            GlobalModerationAction.Reinstate,
            normalizedReasonCode,
            when,
            by));
        MarkUpdated(when, by);
    }

    private string ValidateModerationTransition(string reasonCode, DateTime when, Guid by)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("Deleted identities cannot be moderated.");
        }

        if (by == Guid.Empty)
        {
            throw new ArgumentException("A moderating user is required.", nameof(by));
        }

        if (when.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A UTC moderation timestamp is required.", nameof(when));
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("A moderation reason code is required.", nameof(reasonCode));
        }

        string normalizedReasonCode = reasonCode.Trim();
        if (normalizedReasonCode.Length > 128)
        {
            throw new ArgumentException("A moderation reason code must be 128 characters or fewer.", nameof(reasonCode));
        }

        return normalizedReasonCode;
    }

    private void MarkUpdated(DateTime when, Guid by)
    {
        UpdatedAt = when;
        UpdatedBy = by;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}
