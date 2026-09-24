using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class EventResourceGovernanceService(ISettingsClient client) : IEventResourceGovernanceService
{
    private const string Category = "EventResources";

    public Task<HalResourceOfSettingGroupResponseDto> GetAsync(bool instanceScope, CancellationToken cancellationToken = default) =>
        instanceScope
            ? client.GetInstanceEventResourceSettingsAsync(cancellationToken: cancellationToken)
            : client.GetTenantScopedSettingsAsync(Category, cancellationToken: cancellationToken);

    public Task<BatchUpdateResponseDto> UpdateAsync(bool instanceScope, UpdateSettingBatchDto batch, CancellationToken cancellationToken = default)
    {
        batch.Mode = BatchUpdateMode.Strict;
        return instanceScope
            ? client.UpdateInstanceEventResourceSettingsAsync(batch, cancellationToken: cancellationToken)
            : client.UpdateTenantSettingsBatchAsync(Category, batch, cancellationToken: cancellationToken);
    }
}
