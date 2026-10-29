using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Secrets;

public sealed record AuthenticationCredentialProfile(
    AuthenticationCredentialCutover.CredentialAuthority Authority, Guid Revision);

/// <summary>Selected authority and genuine revision absence are metadata, never credential readiness.</summary>
public sealed record AuthenticationCredentialProfileObservation(
    AuthenticationCredentialCutover.CredentialAuthority Authority, Guid? Revision);

/// <summary>
/// Reads independent, deployment-owned selected-authority metadata locally.
/// Never acquires credentials or contacts a provider.
/// </summary>
public interface IAuthenticationCredentialProfileReader
{
    AuthenticationCredentialProfile ReadCurrent();
    AuthenticationCredentialProfileObservation ObserveCurrent();
}
