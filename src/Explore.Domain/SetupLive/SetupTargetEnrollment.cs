namespace Explore.Domain.SetupLive;

using Explore.Domain.Interfaces;

public enum SetupEnrollmentState
{
    Active = 1,
    Revoked = 2,
    Expired = 3
}

public sealed class SetupTargetEnrollment :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private SetupTargetEnrollment()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; private set; }
    public string ChallengeDigest { get; private set; } = string.Empty;
    public string CapabilityDigest { get; private set; } = string.Empty;
    public string ScopeDigest { get; private set; } = string.Empty;
    public long Generation { get; private set; }
    public SetupEnrollmentState State { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? ExpiredAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static SetupTargetEnrollment Create(
        Guid id,
        Guid tenantId,
        Guid actorId,
        string challengeDigest,
        string capabilityDigest,
        string scopeDigest,
        DateTime createdAt,
        DateTime expiresAt)
    {
        RequireVersion7(id, nameof(id));
        RequireVersion7(tenantId, nameof(tenantId));
        RequireVersion7(actorId, nameof(actorId));
        RequireDigest(challengeDigest, nameof(challengeDigest));
        RequireDigest(capabilityDigest, nameof(capabilityDigest));
        RequireDigest(scopeDigest, nameof(scopeDigest));
        RequireUtc(createdAt, nameof(createdAt));
        RequireUtc(expiresAt, nameof(expiresAt));
        if (expiresAt <= createdAt)
            throw new ArgumentException(
                "Enrollment expiry must follow creation.",
                nameof(expiresAt));

        return new SetupTargetEnrollment
        {
            Id = id,
            TenantId = tenantId,
            ActorId = actorId,
            ChallengeDigest = challengeDigest,
            CapabilityDigest = capabilityDigest,
            ScopeDigest = scopeDigest,
            Generation = 1,
            State = SetupEnrollmentState.Active,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    public bool RotateCapability(
        string capabilityDigest,
        DateTime expiresAt,
        DateTime observedAt)
    {
        RequireDigest(capabilityDigest, nameof(capabilityDigest));
        RequireUtc(expiresAt, nameof(expiresAt));
        RequireUtc(observedAt, nameof(observedAt));
        if (observedAt < CreatedAt || observedAt >= ExpiresAt)
            throw new ArgumentException(
                "Capability rotation must observe an unexpired enrollment.",
                nameof(observedAt));
        if (expiresAt <= ExpiresAt)
            throw new ArgumentException(
                "Capability rotation must extend enrollment expiry.",
                nameof(expiresAt));
        if (State != SetupEnrollmentState.Active)
            throw new InvalidOperationException(
                "Only an active enrollment can rotate capability.");
        if (Generation == long.MaxValue)
            throw new InvalidOperationException(
                "Enrollment generation is exhausted.");

        CapabilityDigest = capabilityDigest;
        ExpiresAt = expiresAt;
        Generation++;
        return true;
    }

    public bool Revoke(DateTime observedAt)
    {
        RequireTransitionTime(observedAt, nameof(observedAt));
        if (State == SetupEnrollmentState.Revoked)
            return false;
        if (State != SetupEnrollmentState.Active || observedAt >= ExpiresAt)
            throw new InvalidOperationException(
                "Only an active enrollment can be revoked.");

        State = SetupEnrollmentState.Revoked;
        RevokedAt = observedAt;
        return true;
    }

    public bool Expire(DateTime observedAt)
    {
        RequireTransitionTime(observedAt, nameof(observedAt));
        if (State == SetupEnrollmentState.Expired)
            return false;
        if (State != SetupEnrollmentState.Active)
            throw new InvalidOperationException(
                "Only an active enrollment can expire.");
        if (observedAt < ExpiresAt)
            throw new ArgumentException(
                "Enrollment cannot expire before its expiry boundary.",
                nameof(observedAt));

        State = SetupEnrollmentState.Expired;
        ExpiredAt = observedAt;
        return true;
    }

    public bool IsAvailable(
        Guid tenantId,
        Guid actorId,
        long generation,
        DateTime observedAt)
    {
        RequireVersion7(tenantId, nameof(tenantId));
        RequireVersion7(actorId, nameof(actorId));
        RequireTransitionTime(observedAt, nameof(observedAt));

        return State == SetupEnrollmentState.Active
            && TenantId == tenantId
            && ActorId == actorId
            && Generation == generation
            && observedAt < ExpiresAt;
    }

    private void RequireTransitionTime(DateTime value, string parameterName)
    {
        RequireUtc(value, parameterName);
        if (value < CreatedAt)
            throw new ArgumentException(
                "Enrollment observation cannot precede creation.",
                parameterName);
    }

    private static void RequireVersion7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                parameterName);
    }

    private static void RequireDigest(string value, string parameterName)
    {
        if (value is null
            || value.Length != 64
            || value.Any(character =>
                character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Evidence must be a lowercase SHA-256 digest.",
                parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }

    private static void RequirePositive(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentException("Value must be positive.", parameterName);
    }
}
