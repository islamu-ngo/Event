using Explore.Domain.Interfaces;

namespace Explore.Domain;

[Flags]
public enum AdmissionScannerCapabilityAction
{
    None = 0,
    CheckIn = 1,
    Undo = 2
}

public enum AdmissionScannerCapabilityRevocationTransition
{
    Revoked = 1,
    AlreadyRevoked = 2
}

public sealed class AdmissionScannerCapability : ITenantEntity, IConcurrencyAware
{
    private const int MaximumDeviceLabelLength = 128;
    private const int MaximumDigestLength = 256;
    private const int MaximumRevocationReasonLength = 200;
    private Guid _tenantId;

    private AdmissionScannerCapability()
    {
    }

    private AdmissionScannerCapability(
        Guid id,
        Guid tenantId,
        Guid issueRequestId,
        Guid eventId,
        Guid admissionTargetId,
        int lookupKeyVersion,
        string lookupDigest,
        string deviceLabel,
        AdmissionScannerCapabilityAction actions,
        DateTime expiresAt,
        Guid issuedByActorId,
        DateTime issuedAt)
    {
        Id = RequireUuidV7(id, nameof(id));
        TenantId = RequireUuidV7(tenantId, nameof(tenantId));
        IssueRequestId = RequireUuidV7(issueRequestId, nameof(issueRequestId));
        EventId = RequireUuidV7(eventId, nameof(eventId));
        AdmissionTargetId = RequireUuidV7(admissionTargetId, nameof(admissionTargetId));
        IssuedByActorId = RequireUuidV7(issuedByActorId, nameof(issuedByActorId));
        if (lookupKeyVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lookupKeyVersion));
        }

        LookupDigest = NormalizeBoundedRequired(lookupDigest, MaximumDigestLength, nameof(lookupDigest));
        DeviceLabel = NormalizeBoundedRequired(deviceLabel, MaximumDeviceLabelLength, nameof(deviceLabel));
        Actions = ValidateActions(actions);
        IssuedAt = RequireUtc(issuedAt, nameof(issuedAt));
        ExpiresAt = RequireUtc(expiresAt, nameof(expiresAt));
        if (ExpiresAt <= IssuedAt)
        {
            throw new ArgumentException("Scanner capability expiry must follow issuance.", nameof(expiresAt));
        }

        LookupKeyVersion = lookupKeyVersion;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionScannerCapability));
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionScannerCapability));
    }

    public Guid IssueRequestId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid AdmissionTargetId { get; private set; }
    public int LookupKeyVersion { get; private set; }
    public string LookupDigest { get; private set; } = string.Empty;
    public string DeviceLabel { get; private set; } = string.Empty;
    public AdmissionScannerCapabilityAction Actions { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public Guid IssuedByActorId { get; private set; }
    public DateTime IssuedAt { get; private set; }
    public Guid? RevokedByActorId { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static AdmissionScannerCapability Issue(
        Guid id,
        Guid tenantId,
        Guid issueRequestId,
        Guid eventId,
        Guid admissionTargetId,
        int lookupKeyVersion,
        string lookupDigest,
        string deviceLabel,
        AdmissionScannerCapabilityAction actions,
        DateTime expiresAt,
        Guid issuedByActorId,
        DateTime issuedAt) => new(
            id,
            tenantId,
            issueRequestId,
            eventId,
            admissionTargetId,
            lookupKeyVersion,
            lookupDigest,
            deviceLabel,
            actions,
            expiresAt,
            issuedByActorId,
            issuedAt);

    public bool IsActiveAt(DateTime evaluatedAt)
    {
        DateTime utc = RequireUtc(evaluatedAt, nameof(evaluatedAt));
        return RevokedAt is null && utc < ExpiresAt;
    }

    public bool Permits(
        Guid admissionTargetId,
        AdmissionScannerCapabilityAction action,
        DateTime evaluatedAt) =>
        IsSingleAction(action) &&
        AdmissionTargetId == admissionTargetId &&
        Actions.HasFlag(action) &&
        IsActiveAt(evaluatedAt);

    public AdmissionScannerCapabilityRevocationTransition Revoke(
        Guid revokedByActorId,
        string reason,
        DateTime revokedAt)
    {
        if (RevokedAt.HasValue)
        {
            return AdmissionScannerCapabilityRevocationTransition.AlreadyRevoked;
        }

        Guid actorId = RequireUuidV7(revokedByActorId, nameof(revokedByActorId));
        DateTime utc = RequireUtc(revokedAt, nameof(revokedAt));
        if (utc < IssuedAt)
        {
            throw new ArgumentException("Scanner capability revocation cannot precede issuance.", nameof(revokedAt));
        }

        RevokedByActorId = actorId;
        RevocationReason = NormalizeBoundedRequired(reason, MaximumRevocationReasonLength, nameof(reason));
        RevokedAt = utc;
        return AdmissionScannerCapabilityRevocationTransition.Revoked;
    }

    public override string ToString() =>
        $"AdmissionScannerCapability({Id}, event={EventId}, target={AdmissionTargetId}, revoked={RevokedAt.HasValue}, <redacted>)";

    private static AdmissionScannerCapabilityAction ValidateActions(AdmissionScannerCapabilityAction actions)
    {
        const AdmissionScannerCapabilityAction all =
            AdmissionScannerCapabilityAction.CheckIn | AdmissionScannerCapabilityAction.Undo;
        if (actions == AdmissionScannerCapabilityAction.None || (actions & ~all) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actions));
        }

        return actions;
    }

    private static bool IsSingleAction(AdmissionScannerCapabilityAction action) => action is
        AdmissionScannerCapabilityAction.CheckIn or AdmissionScannerCapabilityAction.Undo;

    private static string NormalizeBoundedRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static Guid RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Scanner capability lineage must use UUIDv7 values.", parameterName);
        }

        return value;
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Scanner capability timestamps must be non-default UTC values.", parameterName);
        }

        return value;
    }
}
