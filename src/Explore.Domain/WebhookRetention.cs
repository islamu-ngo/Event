using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class WebhookRetentionSubjectKindLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookRetentionSubjectKind
{
    OutgoingMessage = 1,
    IncomingMessage = 2,
    DeliveryAttempt = 3,
    ProviderPublication = 4,
    AdministrativeAudit = 5
}

public sealed class WebhookRetentionHold : ITenantEntity, IAuditableEntity
{
    public const int MaxReasonCodeLength = 200;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public int SubjectKindId { get; private set; }
    public WebhookRetentionSubjectKindLookup SubjectKindLookup { get; private set; } = null!;
    public Guid SubjectId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTime PlacedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    [NotMapped]
    public WebhookRetentionSubjectKind SubjectKind => (WebhookRetentionSubjectKind)SubjectKindId;

    public static WebhookRetentionHold Create(
        Guid tenantId,
        WebhookRetentionSubjectKind subjectKind,
        Guid subjectId,
        string reasonCode,
        DateTime placedAt,
        DateTime? expiresAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(subjectId, Guid.Empty);
        if (!Enum.IsDefined(subjectKind))
        {
            throw new ArgumentOutOfRangeException(nameof(subjectKind));
        }

        RequireUtc(placedAt, nameof(placedAt));
        if (expiresAt is { } expiry)
        {
            RequireUtc(expiry, nameof(expiresAt));
            if (expiry <= placedAt)
            {
                throw new ArgumentOutOfRangeException(nameof(expiresAt));
            }
        }

        return new WebhookRetentionHold
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SubjectKindId = (int)subjectKind,
            SubjectId = subjectId,
            ReasonCode = NormalizeReasonCode(reasonCode),
            PlacedAt = placedAt,
            ExpiresAt = expiresAt,
            CreatedAt = placedAt
        };
    }

    public bool IsActiveAt(DateTime observedAt)
    {
        RequireUtc(observedAt, nameof(observedAt));
        return ReleasedAt is null && (ExpiresAt is null || ExpiresAt > observedAt);
    }

    public void Release(DateTime releasedAt)
    {
        RequireUtc(releasedAt, nameof(releasedAt));
        if (ReleasedAt is not null)
        {
            return;
        }

        if (releasedAt < PlacedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(releasedAt));
        }

        ReleasedAt = releasedAt;
        UpdatedAt = releasedAt;
    }

    private static string NormalizeReasonCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxReasonCodeLength || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.' and not ':'))
        {
            throw new ArgumentException("Retention hold reason codes must be bounded normalized identifiers.", nameof(value));
        }

        return normalized;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", parameterName);
        }
    }
}
