using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Finite, revocable read purpose for one adjudicated case generation and named Human.
/// This structural record does not establish custody-admin authority or grant allegation/material access.
/// </summary>
public sealed class ModerationAdjudicatedCaseReadGrant : ITenantEntity
{
    private ModerationAdjudicatedCaseReadGrant() { }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public Guid IssuerUserId { get; private set; }
    public Guid IssuerRoleGrantId { get; private set; }
    public bool IssuerPlatformGrant { get; private set; }
    public Guid OriginalEffectId { get; private set; }
    public string OriginalSnapshotHash { get; private set; } = string.Empty;
    public string OriginalEffectBindingHash { get; private set; } = string.Empty;
    public DateTimeOffset OriginalRetainUntil { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevocationOperationId { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public Guid? RevokedByRoleGrantId { get; private set; }
    public bool? RevokedByPlatformGrant { get; private set; }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId) throw new InvalidOperationException("Adjudicated-case read grant tenant is immutable.");
        }
    }

    public static ModerationAdjudicatedCaseReadGrant Grant(
        Guid id, Guid hostBindingId, Guid trustDomainId, Guid tenantId, Guid caseId,
        long caseRevision, Guid recipientUserId, Guid issuerUserId, Guid issuerRoleGrantId,
        bool issuerPlatformGrant, Guid originalEffectId, string originalSnapshotHash,
        string originalEffectBindingHash, DateTimeOffset originalRetainUntil,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var grant = new ModerationAdjudicatedCaseReadGrant
        {
            Id = id,
            HostBindingId = hostBindingId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            CaseId = caseId,
            CaseRevision = caseRevision,
            RecipientUserId = recipientUserId,
            IssuerUserId = issuerUserId,
            IssuerRoleGrantId = issuerRoleGrantId,
            IssuerPlatformGrant = issuerPlatformGrant,
            OriginalEffectId = originalEffectId,
            OriginalSnapshotHash = originalSnapshotHash,
            OriginalEffectBindingHash = originalEffectBindingHash,
            OriginalRetainUntil = originalRetainUntil,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            Revision = 1
        };
        grant.ValidatePersistedState();
        return grant;
    }

    public bool IsCurrentAt(DateTimeOffset now)
    {
        ValidatePersistedState();
        RequireInstant(now, nameof(now));
        return RevokedAt is null && IssuedAt <= now && now < ExpiresAt && now < OriginalRetainUntil;
    }

    public void Revoke(Guid operationId, long expectedRevision, Guid adminUserId,
        Guid adminRoleGrantId, bool platform, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != 1 || Revision != 1 || RevokedAt is not null)
            throw new InvalidOperationException("Revocation requires the exact unrevoked adjudicated-case read grant generation.");
        ModerationHostBinding.RequireUuidV7(operationId, nameof(operationId));
        RequireInstant(now, nameof(now));
        if (operationId == Id || adminUserId == Guid.Empty || adminRoleGrantId == Guid.Empty || now < IssuedAt)
            throw new ArgumentException("Revocation requires a distinct operation, accountable native admin and non-regressing time.");

        RevocationOperationId = operationId;
        RevokedByUserId = adminUserId;
        RevokedByRoleGrantId = adminRoleGrantId;
        RevokedByPlatformGrant = platform;
        RevokedAt = now;
        Revision = 2;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(OriginalEffectId, nameof(OriginalEffectId));
        if (HostBindingId == Guid.Empty || TrustDomainId == Guid.Empty || TenantId == Guid.Empty
            || CaseId == Guid.Empty || CaseRevision is < 1 or long.MaxValue
            || RecipientUserId == Guid.Empty || IssuerUserId == Guid.Empty || IssuerRoleGrantId == Guid.Empty
            || !IsCanonicalHash(OriginalSnapshotHash) || !IsCanonicalHash(OriginalEffectBindingHash))
            throw new ArgumentException("Adjudicated-case read grants require exact native custody, case, Human, issuer and original effect bindings.");
        RequireFiniteUtc(OriginalRetainUntil, nameof(OriginalRetainUntil));
        RequireInstant(IssuedAt, nameof(IssuedAt));
        RequireInstant(ExpiresAt, nameof(ExpiresAt));
        if (ExpiresAt <= IssuedAt || ExpiresAt > OriginalRetainUntil)
            throw new ArgumentException("Adjudicated-case read validity must be positive and cannot exceed original retention.");

        if (RevokedAt is null)
        {
            if (Revision != 1 || RevocationOperationId is not null || RevokedByUserId is not null
                || RevokedByRoleGrantId is not null || RevokedByPlatformGrant is not null)
                throw new ArgumentException("An unrevoked adjudicated-case read grant cannot retain revocation state.");
            return;
        }

        ModerationHostBinding.RequireUuidV7(RevocationOperationId ?? Guid.Empty, nameof(RevocationOperationId));
        RequireInstant(RevokedAt.Value, nameof(RevokedAt));
        if (Revision != 2 || RevocationOperationId == Id || RevokedByUserId is null
            || RevokedByUserId == Guid.Empty || RevokedByRoleGrantId is null || RevokedByRoleGrantId == Guid.Empty
            || RevokedByPlatformGrant is null || RevokedAt.Value < IssuedAt)
            throw new ArgumentException("A revoked adjudicated-case read grant requires complete accountable revocation without regressing time.");
    }

    private static bool IsCanonicalHash(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    private static void RequireInstant(DateTimeOffset value, string parameterName)
    {
        RequireFiniteUtc(value, parameterName);
        if (value.Ticks % TimeSpan.TicksPerMicrosecond != 0)
            throw new ArgumentException("Grant instants must be finite UTC values with microsecond precision.", parameterName);
    }

    private static void RequireFiniteUtc(DateTimeOffset value, string parameterName)
    {
        if (value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue || value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Original custody instants must be finite UTC values.", parameterName);
    }

    public override string ToString() => nameof(ModerationAdjudicatedCaseReadGrant);
}
