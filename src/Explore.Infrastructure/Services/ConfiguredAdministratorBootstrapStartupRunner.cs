using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public sealed class ConfiguredAdministratorBootstrapStartupRunner(
    ConfiguredAdministratorBootstrapProvider provider,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        _ = await unitOfWork.ExecuteBootstrapConvergenceAsync(
            async token =>
            {
                InstanceBootstrapState? current =
                    await bootstrapRepository.GetCurrentForUpdate(token);
                if (current?.Status == InstanceBootstrapStatus.Completed)
                {
                    return PreparationOutcome.CompletedFinal;
                }

                ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot snapshot =
                    provider.ReadConfiguration();
                DateTime preparedAt = timeProvider.GetUtcNow().UtcDateTime;
                Guid replacementId = Guid.CreateVersion7(new DateTimeOffset(preparedAt));

                if (snapshot.Mode == InstanceBootstrapMode.Interactive)
                {
                    if (current is null)
                    {
                        return PreparationOutcome.Interactive;
                    }

                    if (current.Status == InstanceBootstrapStatus.Pending
                        && current.Mode == InstanceBootstrapMode.Interactive
                        && current.DeploymentMode == snapshot.DeploymentMode)
                    {
                        return PreparationOutcome.Converged;
                    }

                    throw Failure("instance_bootstrap_interactive_state_conflict");
                }

                if (current is null)
                {
                    await bootstrapRepository.Create(CreatePending(snapshot, replacementId, preparedAt));
                    return PreparationOutcome.Created;
                }

                if (current.Status != InstanceBootstrapStatus.Pending
                    || current.Mode != InstanceBootstrapMode.ConfiguredAdministrator)
                {
                    throw Failure("instance_bootstrap_persisted_state_invalid");
                }

                bool exactGeneration = current.Generation == snapshot.Generation;
                bool exactConfiguration = current.ProviderKind == snapshot.ProviderKind
                    && current.DeploymentMode == snapshot.DeploymentMode
                    && string.Equals(
                        current.ConfigurationFingerprint,
                        snapshot.ConfigurationFingerprint,
                        StringComparison.Ordinal)
                    && string.Equals(
                        current.SelectorFingerprint,
                        snapshot.SelectorFingerprint,
                        StringComparison.Ordinal);
                if (exactGeneration && exactConfiguration)
                {
                    return PreparationOutcome.Converged;
                }

                if (snapshot.Generation <= current.Generation)
                {
                    throw Failure(exactGeneration
                        ? "instance_bootstrap_same_generation_drift"
                        : "instance_bootstrap_generation_regression");
                }

                InstanceBootstrapState replacement = current.Supersede(
                    replacementId,
                    snapshot.ProviderKind!.Value,
                    snapshot.DeploymentMode,
                    snapshot.Generation,
                    snapshot.ConfigurationFingerprint!,
                    snapshot.SelectorFingerprint!,
                    preparedAt);
                await bootstrapRepository.Update(current);
                await bootstrapRepository.Create(replacement);
                return PreparationOutcome.Superseded;
            },
            cancellationToken);
    }

    private static InstanceBootstrapState CreatePending(
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot snapshot,
        Guid id,
        DateTime preparedAt) =>
        InstanceBootstrapState.CreateConfiguredAdministratorPending(
            id,
            snapshot.ProviderKind!.Value,
            snapshot.DeploymentMode,
            snapshot.Generation,
            snapshot.ConfigurationFingerprint!,
            snapshot.SelectorFingerprint!,
            preparedAt);

    private static ConfiguredAdministratorBootstrapException Failure(string reasonCode) => new(reasonCode);

    private enum PreparationOutcome
    {
        Interactive,
        Created,
        Converged,
        Superseded,
        CompletedFinal
    }
}
