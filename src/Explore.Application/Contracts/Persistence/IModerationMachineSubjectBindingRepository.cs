using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Owns explicit credential-to-service provenance under the installed host authority.
/// A binding does not inherit its owner's identity or authorize a capability.
/// </summary>
public interface IModerationMachineSubjectBindingRepository
{
    Task<ModerationMachineSubjectBindingResult> EstablishAsync(
        ModerationMachineSubjectInstallation installation, CancellationToken cancellationToken = default);

    Task<ModerationMachineSubjectBinding?> ReadCurrentAsync(
        string keyId, ModerationMachineCredentialSource credentialSource,
        CancellationToken cancellationToken = default);
}

public sealed record ModerationMachineSubjectInstallation(
    Guid Id, Guid HostBindingId, ModerationMachineCredentialSource CredentialSource,
    string KeyId, Guid? AuthorityTenantId, ExternalApiKeyOwnerType OwnerType,
    Guid OwnerId, Guid ServicePrincipalId);

/// <summary>Successful outcomes carry the native binding entity, never presentation data.</summary>
public sealed record ModerationMachineSubjectBindingResult
{
    private ModerationMachineSubjectBindingResult(
        ModerationAuthoritySourceOutcome outcome, ModerationMachineSubjectBinding? binding)
    {
        Outcome = outcome;
        Binding = binding;
    }

    public ModerationAuthoritySourceOutcome Outcome { get; }
    public ModerationMachineSubjectBinding? Binding { get; }

    public static ModerationMachineSubjectBindingResult Established(ModerationMachineSubjectBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(ModerationAuthoritySourceOutcome.Established, binding);
    }

    public static ModerationMachineSubjectBindingResult Unavailable() =>
        new(ModerationAuthoritySourceOutcome.Unavailable, null);
    public static ModerationMachineSubjectBindingResult Unauthenticated() =>
        new(ModerationAuthoritySourceOutcome.Unauthenticated, null);
    public static ModerationMachineSubjectBindingResult Denied() =>
        new(ModerationAuthoritySourceOutcome.Denied, null);
    public static ModerationMachineSubjectBindingResult Conflict() =>
        new(ModerationAuthoritySourceOutcome.Conflict, null);
}
