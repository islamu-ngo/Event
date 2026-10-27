using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;

namespace Explore.Infrastructure.Secrets;

public sealed class AuthenticationCredentialProfileReader(
    SecretAuthorityAcquisition owner) : IAuthenticationCredentialProfileReader
{
    public AuthenticationCredentialProfile ReadCurrent()
    {
        if (owner.Selection.AuthenticationProfileRevision is not Guid revision || revision == Guid.Empty)
            throw new InvalidOperationException("authentication_credential_profile_unavailable");
        _ = owner.NativeClient;
        var authority = owner.Provider switch
        {
            SecretAuthorityKind.Environment => AuthenticationCredentialCutover.CredentialAuthority.Environment,
            SecretAuthorityKind.UserSecrets => AuthenticationCredentialCutover.CredentialAuthority.UserSecrets,
            SecretAuthorityKind.Infisical => AuthenticationCredentialCutover.CredentialAuthority.Infisical,
            _ => throw new InvalidOperationException("authentication_credential_profile_authority_invalid")
        };
        return new(authority, revision);
    }
}
