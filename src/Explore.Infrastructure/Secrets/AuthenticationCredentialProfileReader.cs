using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;

namespace Explore.Infrastructure.Secrets;

public sealed class AuthenticationCredentialProfileReader(
    SecretAuthorityAcquisition owner) : IAuthenticationCredentialProfileReader
{
    public AuthenticationCredentialProfile ReadCurrent()
    {
        var observation = ObserveCurrent();
        if (observation.Revision is not Guid revision)
            throw new InvalidOperationException("authentication_credential_profile_unavailable");
        return new(observation.Authority, revision);
    }

    public AuthenticationCredentialProfileObservation ObserveCurrent()
    {
        _ = owner.NativeClient;
        var authority = owner.Provider switch
        {
            SecretAuthorityKind.Environment => AuthenticationCredentialCutover.CredentialAuthority.Environment,
            SecretAuthorityKind.UserSecrets => AuthenticationCredentialCutover.CredentialAuthority.UserSecrets,
            SecretAuthorityKind.Infisical => AuthenticationCredentialCutover.CredentialAuthority.Infisical,
            _ => throw new InvalidOperationException("authentication_credential_profile_authority_invalid")
        };
        Guid? revision = owner.Selection.AuthenticationProfileRevision;
        if (revision == Guid.Empty)
            throw new InvalidOperationException("authentication_credential_profile_unavailable");
        return new(authority, revision);
    }
}
