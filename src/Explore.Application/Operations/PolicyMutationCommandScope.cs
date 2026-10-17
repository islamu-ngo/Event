using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Localization.Requests.Commands;

namespace Explore.Application.Operations;

internal static class PolicyMutationCommandScope
{
    internal static bool Handles(Type request) => request == typeof(UpdateLocalizationGovernanceCommand);

    internal static ValueTask<PolicyMutationScope?> SelectAsync(
        object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<PolicyMutationScope?>(request is UpdateLocalizationGovernanceCommand
            ? PolicyMutationScope.Instance : null);
    }
}
