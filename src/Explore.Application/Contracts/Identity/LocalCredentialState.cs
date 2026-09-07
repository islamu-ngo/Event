// ABOUTME: Defines mandatory typed Local credential state stored in the existing Identity token slot.
// ABOUTME: Validates versioned nonsecret metadata without treating missing or unknown state as ready.

using System.Text.Json.Serialization;

namespace Explore.Application.Contracts.Identity;

public enum LocalCredentialState
{
    ProvisioningPending = 1,
    ChangeRequired = 2,
    Ready = 3
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalCredentialStateMetadata
{
    public const int CurrentVersion = 1;
    public const string TokenLoginProvider = "Explore.LocalIdentity";
    public const string TokenName = "CredentialState";

    [JsonConstructor]
    public LocalCredentialStateMetadata(
        int version,
        LocalCredentialState state,
        Guid operationId,
        Guid applicationUserId)
    {
        if (version != CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Unsupported Local credential metadata version.");
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Unknown Local credential state.");
        }

        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A credential operation identifier is required.", nameof(operationId));
        }

        if (applicationUserId == Guid.Empty)
        {
            throw new ArgumentException("An application user identifier is required.", nameof(applicationUserId));
        }

        Version = version;
        State = state;
        OperationId = operationId;
        ApplicationUserId = applicationUserId;
    }

    public int Version { get; }
    public LocalCredentialState State { get; }
    public Guid OperationId { get; }
    public Guid ApplicationUserId { get; }
}
