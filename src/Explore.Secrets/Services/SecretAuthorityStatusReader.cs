using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Services;

public sealed class SecretAuthorityStatusReader(
    ISecretProvider provider,
    IOptions<SecretProviderOptions> options) : ISecretAuthorityStatusReader
{
    public async Task<SecretAuthorityStatusSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        SecretProviderOptions configured = options.Value;
        if (configured.Provider is not (
            SecretProviderType.Environment
            or SecretProviderType.Infisical
            or SecretProviderType.UserSecrets))
        {
            return new(configured.Provider.ToString(), "invalid", "select_supported_secret_authority");
        }

        if (configured.Provider == SecretProviderType.Infisical && !HasInfisicalBootstrap(configured.Infisical))
        {
            return new("Infisical", "required", "configure_selected_secret_authority");
        }

        try
        {
            ProviderHealthInfo health = await provider.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            return health.IsHealthy
                ? new(configured.Provider.ToString(), "configured", "none")
                : new(configured.Provider.ToString(), "degraded", "restore_selected_secret_authority");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Control-plane boundary translates provider failure to a bounded status.
        catch (Exception)
#pragma warning restore CA1031
        {
            return new(configured.Provider.ToString(), "degraded", "restore_selected_secret_authority");
        }
    }

    private static bool HasInfisicalBootstrap(InfisicalOptions infisical) =>
        !string.IsNullOrWhiteSpace(infisical.Url)
        && !string.IsNullOrWhiteSpace(infisical.ProjectId)
        && !string.IsNullOrWhiteSpace(infisical.ClientId)
        && !string.IsNullOrWhiteSpace(infisical.ClientSecret)
        && !string.IsNullOrWhiteSpace(infisical.Environment);
}
