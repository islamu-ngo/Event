using Explore.Domain.Enums;

namespace Explore.Domain;

public class InstanceBootstrapState
{
    private InstanceBootstrapState()
    {
    }

    public Guid Id { get; private set; }
    public InstanceBootstrapStatus Status { get; private set; }
    public InstanceBootstrapMode Mode { get; private set; }
    public AuthenticationProviderKind? ProviderKind { get; private set; }
    public DeploymentMode DeploymentMode { get; private set; }
    public long Generation { get; private set; }
    public string? ConfigurationFingerprint { get; private set; }
    public string? SelectorFingerprint { get; private set; }
    public string? CompletedIdentityFingerprint { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SupersededAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public Guid? CompletedByUserId { get; private set; }

    public static InstanceBootstrapState CreateInteractivePending(
        Guid id,
        DeploymentMode deploymentMode,
        DateTime createdAt)
    {
        RequireUuidV7(id, nameof(id));
        RequireDefined(deploymentMode, nameof(deploymentMode));
        RequireUtc(createdAt, nameof(createdAt));

        return new()
        {
            Id = id,
            Status = InstanceBootstrapStatus.Pending,
            Mode = InstanceBootstrapMode.Interactive,
            DeploymentMode = deploymentMode,
            Generation = 1,
            CreatedAt = createdAt
        };
    }

    public static InstanceBootstrapState CreateConfiguredAdministratorPending(
        Guid id,
        AuthenticationProviderKind providerKind,
        DeploymentMode deploymentMode,
        long generation,
        string configurationFingerprint,
        string selectorFingerprint,
        DateTime createdAt)
    {
        RequireUuidV7(id, nameof(id));
        RequireConfiguredProvider(providerKind, nameof(providerKind));
        RequireDefined(deploymentMode, nameof(deploymentMode));
        RequirePositiveGeneration(generation, nameof(generation));
        RequireFingerprint(configurationFingerprint, nameof(configurationFingerprint));
        RequireFingerprint(selectorFingerprint, nameof(selectorFingerprint));
        RequireUtc(createdAt, nameof(createdAt));

        return new()
        {
            Id = id,
            Status = InstanceBootstrapStatus.Pending,
            Mode = InstanceBootstrapMode.ConfiguredAdministrator,
            ProviderKind = providerKind,
            DeploymentMode = deploymentMode,
            Generation = generation,
            ConfigurationFingerprint = configurationFingerprint,
            SelectorFingerprint = selectorFingerprint,
            CreatedAt = createdAt
        };
    }

    public InstanceBootstrapState Supersede(
        Guid replacementId,
        AuthenticationProviderKind providerKind,
        DeploymentMode deploymentMode,
        long replacementGeneration,
        string configurationFingerprint,
        string selectorFingerprint,
        DateTime supersededAt)
    {
        RequireUuidV7(replacementId, nameof(replacementId));
        RequireConfiguredProvider(providerKind, nameof(providerKind));
        RequireDefined(deploymentMode, nameof(deploymentMode));
        RequirePositiveGeneration(replacementGeneration, nameof(replacementGeneration));
        RequireFingerprint(configurationFingerprint, nameof(configurationFingerprint));
        RequireFingerprint(selectorFingerprint, nameof(selectorFingerprint));
        RequireNonRegressingUtc(supersededAt, CreatedAt, nameof(supersededAt));

        if (Status != InstanceBootstrapStatus.Pending
            || Mode != InstanceBootstrapMode.ConfiguredAdministrator)
        {
            throw new InvalidOperationException("Only a pending configured-administrator generation can be superseded.");
        }

        if (replacementGeneration <= Generation)
        {
            throw new InvalidOperationException("A replacement must increase the bootstrap generation.");
        }

        InstanceBootstrapState replacement = CreateConfiguredAdministratorPending(
            replacementId,
            providerKind,
            deploymentMode,
            replacementGeneration,
            configurationFingerprint,
            selectorFingerprint,
            supersededAt);

        Status = InstanceBootstrapStatus.Superseded;
        SupersededAt = supersededAt;
        return replacement;
    }

    public bool CompleteInteractive(Guid completedByUserId, DateTime completedAt)
    {
        RequireUuidV7(completedByUserId, nameof(completedByUserId));
        RequireNonRegressingUtc(completedAt, CreatedAt, nameof(completedAt));

        if (Mode != InstanceBootstrapMode.Interactive)
        {
            throw new InvalidOperationException("Configured-administrator bootstrap requires configured completion.");
        }

        if (Status == InstanceBootstrapStatus.Completed)
        {
            if (CompletedByUserId == completedByUserId)
            {
                return false;
            }

            throw new InvalidOperationException("Completed bootstrap evidence is final.");
        }

        EnsurePending();
        Status = InstanceBootstrapStatus.Completed;
        CompletedByUserId = completedByUserId;
        CompletedAt = completedAt;
        return true;
    }

    public bool CompleteConfiguredAdministrator(
        AuthenticationProviderKind providerKind,
        long generation,
        string identityFingerprint,
        Guid completedByUserId,
        DateTime completedAt)
    {
        RequireConfiguredProvider(providerKind, nameof(providerKind));
        RequirePositiveGeneration(generation, nameof(generation));
        RequireFingerprint(identityFingerprint, nameof(identityFingerprint));
        RequireUuidV7(completedByUserId, nameof(completedByUserId));
        RequireNonRegressingUtc(completedAt, CreatedAt, nameof(completedAt));

        if (Mode != InstanceBootstrapMode.ConfiguredAdministrator)
        {
            throw new InvalidOperationException("Interactive bootstrap requires interactive completion.");
        }

        if (Status == InstanceBootstrapStatus.Completed)
        {
            if (ProviderKind == providerKind
                && Generation == generation
                && CompletedIdentityFingerprint == identityFingerprint
                && CompletedByUserId == completedByUserId)
            {
                return false;
            }

            throw new InvalidOperationException("Completed bootstrap evidence is final.");
        }

        EnsurePending();
        if (ProviderKind != providerKind || Generation != generation)
        {
            throw new InvalidOperationException("Completion does not match the pending provider generation.");
        }

        if (!string.Equals(SelectorFingerprint, identityFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Completion identity does not match the configured selector.");
        }

        Status = InstanceBootstrapStatus.Completed;
        CompletedIdentityFingerprint = identityFingerprint;
        CompletedByUserId = completedByUserId;
        CompletedAt = completedAt;
        return true;
    }

    public bool TransitionDeploymentMode(DeploymentMode deploymentMode)
    {
        RequireDefined(deploymentMode, nameof(deploymentMode));
        if (Status != InstanceBootstrapStatus.Completed)
        {
            throw new InvalidOperationException("Deployment mode can change only after bootstrap completion.");
        }

        if (DeploymentMode == deploymentMode)
        {
            return false;
        }

        DeploymentMode = deploymentMode;
        return true;
    }

    private void EnsurePending()
    {
        if (Status != InstanceBootstrapStatus.Pending)
        {
            throw new InvalidOperationException("Superseded and completed bootstrap generations are final.");
        }
    }

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Identifier must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }

    private static void RequirePositiveGeneration(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Generation must be positive.");
        }
    }

    private static void RequireFingerprint(string value, string parameterName)
    {
        if (value is null || value.Length != 64
            || value.Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
        {
            throw new ArgumentException("Fingerprint must be exactly 64 lowercase hexadecimal characters.", parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }

    private static void RequireNonRegressingUtc(DateTime value, DateTime earliest, string parameterName)
    {
        RequireUtc(value, parameterName);
        if (value < earliest)
        {
            throw new ArgumentException("Timestamp cannot precede generation creation.", parameterName);
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value is outside the closed enum contract.");
        }
    }

    private static void RequireConfiguredProvider(
        AuthenticationProviderKind value,
        string parameterName)
    {
        if (value is not AuthenticationProviderKind.Keycloak
            and not AuthenticationProviderKind.Local
            and not AuthenticationProviderKind.Atproto)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Configured administrator bootstrap supports only Local Identity, Keycloak, or ATProto.");
        }
    }
}
