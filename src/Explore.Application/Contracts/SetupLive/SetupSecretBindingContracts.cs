namespace Explore.Application.Contracts.SetupLive;

public sealed class SetupSecretBindingWriteRequest
{
    public SetupSecretBindingWriteRequest(
        Guid tenantId,
        Guid enrollmentId,
        long enrollmentGeneration,
        Guid operationId,
        Guid bindingId,
        string bindingKey,
        ReadOnlyMemory<byte> secretValue)
    {
        if (tenantId == Guid.Empty
            || tenantId.Version != 7
            || (tenantId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(tenantId));
        if (enrollmentId == Guid.Empty
            || enrollmentId.Version != 7
            || (enrollmentId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(enrollmentId));
        if (enrollmentGeneration <= 0)
            throw new ArgumentException(
                "Value must be positive.",
                nameof(enrollmentGeneration));
        if (operationId == Guid.Empty
            || operationId.Version != 7
            || (operationId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(operationId));
        if (bindingId == Guid.Empty
            || bindingId.Version != 7
            || (bindingId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(bindingId));
        if (!string.Equals(
                bindingKey,
                "setup.signing",
                StringComparison.Ordinal)
            && !string.Equals(
                bindingKey,
                "setup.encryption",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Binding key is not supported.",
                nameof(bindingKey));
        }
        if (secretValue.Length is < 1 or > 65_536)
            throw new ArgumentException(
                "Secret byte length is outside the supported range.",
                nameof(secretValue));

        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        EnrollmentGeneration = enrollmentGeneration;
        OperationId = operationId;
        BindingId = bindingId;
        BindingKey = bindingKey;
        SecretValue = secretValue;
    }

    public Guid TenantId { get; }
    public Guid EnrollmentId { get; }
    public long EnrollmentGeneration { get; }
    public Guid OperationId { get; }
    public Guid BindingId { get; }
    public string BindingKey { get; }
    public ReadOnlyMemory<byte> SecretValue { get; }
}

public enum SetupSecretBindingWriteOutcome
{
    Invalid = 0,
    Ready = 1,
    Unavailable = 2,
    Unauthorized = 3
}

public sealed class SetupSecretBindingCommitmentRequest
{
    public SetupSecretBindingCommitmentRequest(
        Guid tenantId,
        Guid actorId,
        Guid enrollmentId,
        long enrollmentGeneration,
        Guid operationKey,
        string bindingKey,
        ReadOnlyMemory<byte> secretValue)
    {
        if (tenantId == Guid.Empty
            || tenantId.Version != 7
            || (tenantId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(tenantId));
        if (actorId == Guid.Empty
            || actorId.Version != 7
            || (actorId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(actorId));
        if (enrollmentId == Guid.Empty
            || enrollmentId.Version != 7
            || (enrollmentId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(enrollmentId));
        if (enrollmentGeneration <= 0)
            throw new ArgumentException(
                "Value must be positive.",
                nameof(enrollmentGeneration));
        if (operationKey == Guid.Empty
            || operationKey.Version != 7
            || (operationKey.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(operationKey));
        if (!string.Equals(
                bindingKey,
                "setup.signing",
                StringComparison.Ordinal)
            && !string.Equals(
                bindingKey,
                "setup.encryption",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Binding key is not supported.",
                nameof(bindingKey));
        }
        if (secretValue.Length is < 1 or > 65_536)
            throw new ArgumentException(
                "Secret byte length is outside the supported range.",
                nameof(secretValue));

        TenantId = tenantId;
        ActorId = actorId;
        EnrollmentId = enrollmentId;
        EnrollmentGeneration = enrollmentGeneration;
        OperationKey = operationKey;
        BindingKey = bindingKey;
        SecretValue = secretValue;
    }

    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public Guid EnrollmentId { get; }
    public long EnrollmentGeneration { get; }
    public Guid OperationKey { get; }
    public string BindingKey { get; }
    public ReadOnlyMemory<byte> SecretValue { get; }
}

public sealed class SetupSecretBindingCommitment
{
    public SetupSecretBindingCommitment(
        int keyVersion,
        string commitment)
    {
        if (keyVersion <= 0)
            throw new ArgumentException(
                "Value must be positive.",
                nameof(keyVersion));
        if (commitment is null || commitment.Length != 64)
        {
            throw new ArgumentException(
                "Commitment must be lowercase hexadecimal evidence.",
                nameof(commitment));
        }
        foreach (char character in commitment)
        {
            if (character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f'))
            {
                throw new ArgumentException(
                    "Commitment must be lowercase hexadecimal evidence.",
                    nameof(commitment));
            }
        }

        KeyVersion = keyVersion;
        Commitment = commitment;
    }

    public int KeyVersion { get; }
    public string Commitment { get; }
}

public interface ISetupSecretBindingCommitmentAuthority
{
    Task<SetupSecretBindingCommitment> CommitAsync(
        SetupSecretBindingCommitmentRequest request,
        CancellationToken cancellationToken);
}

public sealed class SetupSecretBindingCoordinationRequest
{
    public SetupSecretBindingCoordinationRequest(
        Guid tenantId,
        Guid enrollmentId,
        long enrollmentGeneration)
    {
        if (tenantId == Guid.Empty
            || tenantId.Version != 7
            || (tenantId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(tenantId));
        if (enrollmentId == Guid.Empty
            || enrollmentId.Version != 7
            || (enrollmentId.Variant & 0b1100) != 0b1000)
            throw new ArgumentException(
                "Identity must be a UUIDv7 value.",
                nameof(enrollmentId));
        if (enrollmentGeneration <= 0)
            throw new ArgumentException(
                "Value must be positive.",
                nameof(enrollmentGeneration));

        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        EnrollmentGeneration = enrollmentGeneration;
    }

    public Guid TenantId { get; }
    public Guid EnrollmentId { get; }
    public long EnrollmentGeneration { get; }
}

public interface ISetupSecretBindingOperationCoordinator
{
    Task<IAsyncDisposable> AcquireAsync(
        SetupSecretBindingCoordinationRequest request,
        CancellationToken cancellationToken);
}

public static class SetupSecretBindingContractMetadata
{
    public const string CommitmentAuthorityKey =
        "setup.secret_binding_commitment_hmac_key";
    public const int MilestoneEventId = 19620;
    public const string MilestoneEventName = "SetupLiveMilestone";
    public const string Operation = "secret_binding.write";
    public const string BeforeProviderDispatchMilestone =
        "before_provider_dispatch";
}
