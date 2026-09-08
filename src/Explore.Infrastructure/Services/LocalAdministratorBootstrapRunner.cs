
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public sealed class LocalAdministratorBootstrapRunner(
    ConfiguredAdministratorBootstrapProvider provider,
    IInstanceBootstrapStateRepository bootstrapRepository,
    LocalAdministratorBootstrapOperation operation)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var current = await bootstrapRepository.GetCurrent(cancellationToken);
        if (current?.Status == InstanceBootstrapStatus.Completed)
        {
            await operation.ReconcileCompletedAsync(cancellationToken);
            return;
        }
        var snapshot = provider.ReadConfiguration();
        if (snapshot.ProviderKind != AuthenticationProviderKind.Local)
            return;
        var result = await operation.CompleteConfiguredAsync(snapshot.AccountKey!, cancellationToken);
        if (!result.IsSuccess)
            throw new ConfiguredAdministratorBootstrapException(result.FailureCode ?? "local_bootstrap_failed");
    }
}
