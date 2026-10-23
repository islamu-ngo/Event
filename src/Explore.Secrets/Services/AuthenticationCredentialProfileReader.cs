using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Services;

public sealed class AuthenticationCredentialProfileReader(
    IOptions<SecretProviderOptions> options, IHostEnvironment environment)
    : IAuthenticationCredentialProfileReader
{
    public const string RevisionEnvironmentVariable = "AUTHENTICATION_CREDENTIAL_PROFILE_REVISION";

    public AuthenticationCredentialProfile ReadCurrent()
    {
        if (!Guid.TryParse(Environment.GetEnvironmentVariable(RevisionEnvironmentVariable), out Guid revision)
            || revision == Guid.Empty)
            throw new InvalidOperationException("authentication_credential_profile_unavailable");

        var authority = options.Value.Provider switch
        {
            SecretProviderType.Environment => AuthenticationCredentialCutover.CredentialAuthority.Environment,
            SecretProviderType.Infisical => AuthenticationCredentialCutover.CredentialAuthority.Infisical,
            SecretProviderType.UserSecrets when environment.IsDevelopment() || environment.IsEnvironment("Testing")
                => AuthenticationCredentialCutover.CredentialAuthority.UserSecrets,
            _ => throw new InvalidOperationException("authentication_credential_profile_authority_invalid")
        };
        return new(authority, revision);
    }
}
