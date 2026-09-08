namespace Explore.Domain.SetupLive;

using Explore.Domain.Interfaces;

public enum SetupSecretBindingOperationState
{
    Accepted = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

public enum SetupSecretBindingOperationOutcome
{
    Accepted = 1,
    Ready = 2,
    Unavailable = 3,
    Unauthorized = 4,
    Invalid = 5,
    Cancelled = 6,
    UnavailableEnrollment = 7
}

public sealed class SetupSecretBindingOperation :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private SetupSecretBindingOperation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public long EnrollmentGeneration { get; private set; }
    public Guid OperationKey { get; private set; }
    public string BindingKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public int CommitmentKeyVersion { get; private set; }
    public string SecretValueCommitment { get; private set; } = string.Empty;
    public SetupSecretBindingOperationState State { get; private set; }
    public SetupSecretBindingOperationOutcome Outcome { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? SettledAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public static SetupSecretBindingOperation CreateAccepted(
        Guid id,
        Guid tenantId,
        Guid actorId,
        Guid enrollmentId,
        long enrollmentGeneration,
        Guid operationKey,
        string bindingKey,
        string requestFingerprint,
        int commitmentKeyVersion,
        string secretValueCommitment,
        DateTime createdAt)
    {
        RequireVersion7(id, nameof(id));
        RequireVersion7(tenantId, nameof(tenantId));
        RequireVersion7(actorId, nameof(actorId));
        RequireVersion7(enrollmentId, nameof(enrollmentId));
        RequirePositive(enrollmentGeneration, nameof(enrollmentGeneration));
        RequireVersion7(operationKey, nameof(operationKey));
        RequireBindingKey(bindingKey, nameof(bindingKey));
        RequireDigest(requestFingerprint, nameof(requestFingerprint));
        RequirePositive(commitmentKeyVersion, nameof(commitmentKeyVersion));
        RequireDigest(secretValueCommitment, nameof(secretValueCommitment));
        RequireUtc(createdAt, nameof(createdAt));

        return new SetupSecretBindingOperation
        {
            Id = id,
            TenantId = tenantId,
            ActorId = actorId,
            EnrollmentId = enrollmentId,
            EnrollmentGeneration = enrollmentGeneration,
            OperationKey = operationKey,
            BindingKey = bindingKey,
            RequestFingerprint = requestFingerprint,
            CommitmentKeyVersion = commitmentKeyVersion,
            SecretValueCommitment = secretValueCommitment,
            State = SetupSecretBindingOperationState.Accepted,
            Outcome = SetupSecretBindingOperationOutcome.Accepted,
            CreatedAt = createdAt
        };
    }

    public SetupReplayDecision Match(
        Guid tenantId,
        Guid actorId,
        Guid enrollmentId,
        long enrollmentGeneration,
        Guid operationKey,
        string bindingKey,
        string requestFingerprint,
        int commitmentKeyVersion,
        string secretValueCommitment)
    {
        RequireVersion7(tenantId, nameof(tenantId));
        RequireVersion7(actorId, nameof(actorId));
        RequireVersion7(enrollmentId, nameof(enrollmentId));
        RequirePositive(enrollmentGeneration, nameof(enrollmentGeneration));
        RequireVersion7(operationKey, nameof(operationKey));
        RequireBindingKey(bindingKey, nameof(bindingKey));
        RequireDigest(requestFingerprint, nameof(requestFingerprint));
        RequirePositive(commitmentKeyVersion, nameof(commitmentKeyVersion));
        RequireDigest(secretValueCommitment, nameof(secretValueCommitment));

        return TenantId == tenantId
            && ActorId == actorId
            && EnrollmentId == enrollmentId
            && EnrollmentGeneration == enrollmentGeneration
            && OperationKey == operationKey
            && string.Equals(BindingKey, bindingKey, StringComparison.Ordinal)
            && string.Equals(
                RequestFingerprint,
                requestFingerprint,
                StringComparison.Ordinal)
            && CommitmentKeyVersion == commitmentKeyVersion
            && string.Equals(
                SecretValueCommitment,
                secretValueCommitment,
                StringComparison.Ordinal)
            ? SetupReplayDecision.SameRequest
            : SetupReplayDecision.Conflict;
    }

    public bool CanDispatch(
        SetupTargetEnrollment enrollment,
        DateTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        RequireTransitionTime(observedAt, nameof(observedAt));

        return State == SetupSecretBindingOperationState.Accepted
            && EnrollmentId == enrollment.Id
            && TenantId == enrollment.TenantId
            && ActorId == enrollment.ActorId
            && EnrollmentGeneration == enrollment.Generation
            && enrollment.IsAvailable(
                TenantId,
                ActorId,
                EnrollmentGeneration,
                observedAt);
    }

    public bool Succeed(DateTime settledAt)
    {
        RequireTransitionTime(settledAt, nameof(settledAt));
        if (State == SetupSecretBindingOperationState.Succeeded)
            return false;
        EnsureAccepted();

        State = SetupSecretBindingOperationState.Succeeded;
        Outcome = SetupSecretBindingOperationOutcome.Ready;
        SettledAt = settledAt;
        return true;
    }

    public bool Fail(
        SetupSecretBindingOperationOutcome outcome,
        DateTime settledAt)
    {
        RequireFailureOutcome(outcome, nameof(outcome));
        RequireTransitionTime(settledAt, nameof(settledAt));
        if (State == SetupSecretBindingOperationState.Failed)
        {
            if (Outcome == outcome)
                return false;
            throw new InvalidOperationException(
                "A failed operation cannot change outcome.");
        }
        EnsureAccepted();

        State = SetupSecretBindingOperationState.Failed;
        Outcome = outcome;
        SettledAt = settledAt;
        return true;
    }

    public bool Cancel(DateTime settledAt)
    {
        RequireTransitionTime(settledAt, nameof(settledAt));
        if (State == SetupSecretBindingOperationState.Cancelled)
            return false;
        EnsureAccepted();

        State = SetupSecretBindingOperationState.Cancelled;
        Outcome = SetupSecretBindingOperationOutcome.Cancelled;
        SettledAt = settledAt;
        return true;
    }

    private void EnsureAccepted()
    {
        if (State != SetupSecretBindingOperationState.Accepted)
            throw new InvalidOperationException(
                "Only an accepted operation can settle.");
    }

    private void RequireTransitionTime(DateTime value, string parameterName)
    {
        RequireUtc(value, parameterName);
        if (value < CreatedAt)
            throw new ArgumentException(
                "Operation settlement cannot precede creation.",
                parameterName);
    }

    private static void RequireFailureOutcome(
        SetupSecretBindingOperationOutcome value,
        string parameterName)
    {
        if (value is not SetupSecretBindingOperationOutcome.Unavailable
            and not SetupSecretBindingOperationOutcome.Unauthorized
            and not SetupSecretBindingOperationOutcome.Invalid
            and not SetupSecretBindingOperationOutcome.UnavailableEnrollment)
        {
            throw new ArgumentException(
                "Outcome is not a failure result.",
                parameterName);
        }
    }

    private static void RequireBindingKey(string value, string parameterName)
    {
        if (!string.Equals(value, "setup.signing", StringComparison.Ordinal)
            && !string.Equals(value, "setup.encryption", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Binding key is not supported.",
                parameterName);
        }
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
