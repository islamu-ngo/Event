using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class SupportAccessSession : IAuditableEntity, IConcurrencyAware
{
    public const int MaxReasonCodeLength = 100;
    public const int MaxReasonTextLength = 1000;
    public const int MaxTicketReferenceLength = 200;
    public const int MaxEndReasonTextLength = 200;

    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public User? ActorUser { get; private set; }
    public Guid TargetTenantId { get; private set; }
    public Tenant TargetTenant { get; private set; } = null!;
    public Guid? TargetTenantUserId { get; private set; }
    public TenantUser? TargetTenantUser { get; private set; }
    public int StatusId { get; private set; }
    public SupportAccessSessionStatus Status { get; private set; } = null!;
    public int ModeId { get; private set; }
    public SupportAccessMode Mode { get; private set; } = null!;
    public string ReasonCode { get; private set; } = string.Empty;
    public string ReasonText { get; private set; } = string.Empty;
    public string TicketReference { get; private set; } = string.Empty;
    public Guid? ApprovedByUserId { get; private set; }
    public User? ApprovedByUser { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public int? EndReasonId { get; private set; }
    public SupportAccessEndReason? EndReason { get; private set; }
    public string? EndReasonText { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public bool IsActiveAt(DateTimeOffset nowUtc)
    {
        return ActorUserId.HasValue
            && StatusId == (int)SupportAccessSessionStatusEnum.Active
            && EndedAtUtc is null
            && StartedAtUtc <= nowUtc
            && nowUtc < ExpiresAtUtc;
    }

    public bool AllowsWrites =>
        ModeId == (int)SupportAccessModeEnum.Write && StatusId == (int)SupportAccessSessionStatusEnum.Active;

    public static SupportAccessSession Start(
        Guid actorUserId,
        Guid targetTenantId,
        SupportAccessModeEnum mode,
        string reasonCode,
        string reasonText,
        string ticketReference,
        DateTimeOffset startedAtUtc,
        DateTimeOffset expiresAtUtc,
        Guid? targetTenantUserId = null,
        Guid? approvedByUserId = null)
    {
        EnsureNotEmpty(actorUserId, nameof(actorUserId));
        EnsureNotEmpty(targetTenantId, nameof(targetTenantId));
        EnsureOptionalNotEmpty(targetTenantUserId, nameof(targetTenantUserId));
        EnsureOptionalNotEmpty(approvedByUserId, nameof(approvedByUserId));

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Support-access mode is not valid.");
        }

        if (expiresAtUtc <= startedAtUtc)
        {
            throw new ArgumentException("Support-access expiry must be after the start time.", nameof(expiresAtUtc));
        }

        return new SupportAccessSession
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorUserId,
            TargetTenantId = targetTenantId,
            TargetTenantUserId = targetTenantUserId,
            StatusId = (int)SupportAccessSessionStatusEnum.Active,
            ModeId = (int)mode,
            ReasonCode = NormalizeRequired(reasonCode, MaxReasonCodeLength, nameof(reasonCode)),
            ReasonText = NormalizeRequired(reasonText, MaxReasonTextLength, nameof(reasonText)),
            TicketReference = NormalizeRequired(ticketReference, MaxTicketReferenceLength, nameof(ticketReference)),
            ApprovedByUserId = approvedByUserId,
            StartedAtUtc = startedAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public void Stop(DateTimeOffset stoppedAtUtc, string? endReasonText = null)
    {
        Complete(
            SupportAccessSessionStatusEnum.Stopped,
            SupportAccessEndReasonEnum.UserStopped,
            stoppedAtUtc,
            endReasonText,
            "Only active support-access sessions can be stopped.");
    }

    public void Expire(DateTimeOffset expiredAtUtc, string? endReasonText = null)
    {
        Complete(
            SupportAccessSessionStatusEnum.Expired,
            SupportAccessEndReasonEnum.Expired,
            expiredAtUtc,
            endReasonText,
            "Only active support-access sessions can be expired.");
    }

    public void Revoke(DateTimeOffset revokedAtUtc, SupportAccessEndReasonEnum reason, string? endReasonText = null)
    {
        if (reason is SupportAccessEndReasonEnum.UserStopped or SupportAccessEndReasonEnum.Expired)
        {
            throw new ArgumentException("Revocation requires a revocation-oriented end reason.", nameof(reason));
        }

        Complete(
            SupportAccessSessionStatusEnum.Revoked,
            reason,
            revokedAtUtc,
            endReasonText,
            "Only active support-access sessions can be revoked.");
    }

    private void Complete(
        SupportAccessSessionStatusEnum terminalStatus,
        SupportAccessEndReasonEnum endReason,
        DateTimeOffset endedAtUtc,
        string? endReasonText,
        string inactiveMessage)
    {
        if (StatusId != (int)SupportAccessSessionStatusEnum.Active || EndedAtUtc is not null)
        {
            throw new InvalidOperationException(inactiveMessage);
        }

        if (endedAtUtc < StartedAtUtc)
        {
            throw new ArgumentException("Support-access end time cannot be before the start time.", nameof(endedAtUtc));
        }

        StatusId = (int)terminalStatus;
        EndReasonId = (int)endReason;
        EndedAtUtc = endedAtUtc;
        EndReasonText = NormalizeOptional(endReasonText, MaxEndReasonTextLength, nameof(endReasonText));
    }

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void EnsureOptionalNotEmpty(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required when provided.", parameterName);
        }
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        string normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}
