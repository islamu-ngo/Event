using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.Services;

public sealed class ModerationReconciliationSecretProvider(IConfiguration configuration)
    : IModerationReconciliationSecretProvider
{
    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authority = SecretAuthorityConfiguration.Build(configuration,
            SecretAuthorityConfiguration.GetEnvironmentName(configuration), "/api");
        return ValueTask.FromResult(authority["Moderation:Reconciliation:ApiKey"]);
    }
}
