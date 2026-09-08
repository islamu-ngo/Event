namespace Explore.Domain.SetupLive;

using Explore.Domain.Interfaces;

public enum SetupReplayDecision
{
    SameRequest = 1,
    Conflict = 2
}

public sealed class SetupEnrollmentIssuanceClaim : ITenantEntity
{
    private SetupEnrollmentIssuanceClaim()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; private set; }
    public Guid OperationKey { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public long EnrollmentGeneration { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime ClaimedAt { get; private set; }

    public static SetupEnrollmentIssuanceClaim Create(
        Guid id,
        Guid tenantId,
        Guid actorId,
        Guid operationKey,
        Guid enrollmentId,
        long enrollmentGeneration,
        string requestFingerprint,
        DateTime claimedAt)
    {
        RequireVersion7(id, nameof(id));
        RequireVersion7(tenantId, nameof(tenantId));
        RequireVersion7(actorId, nameof(actorId));
        RequireVersion7(operationKey, nameof(operationKey));
        RequireVersion7(enrollmentId, nameof(enrollmentId));
        RequirePositive(enrollmentGeneration, nameof(enrollmentGeneration));
        RequireDigest(requestFingerprint, nameof(requestFingerprint));
        RequireUtc(claimedAt, nameof(claimedAt));

        return new SetupEnrollmentIssuanceClaim
        {
            Id = id,
            TenantId = tenantId,
            ActorId = actorId,
            OperationKey = operationKey,
            EnrollmentId = enrollmentId,
            EnrollmentGeneration = enrollmentGeneration,
            RequestFingerprint = requestFingerprint,
            ClaimedAt = claimedAt
        };
    }

    public SetupReplayDecision Match(
        Guid tenantId,
        Guid actorId,
        string requestFingerprint)
    {
        RequireVersion7(tenantId, nameof(tenantId));
        RequireVersion7(actorId, nameof(actorId));
        RequireDigest(requestFingerprint, nameof(requestFingerprint));

        return TenantId == tenantId
            && ActorId == actorId
            && string.Equals(
                RequestFingerprint,
                requestFingerprint,
                StringComparison.Ordinal)
            ? SetupReplayDecision.SameRequest
            : SetupReplayDecision.Conflict;
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

    private static void RequirePositive(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentException("Value must be positive.", parameterName);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}
