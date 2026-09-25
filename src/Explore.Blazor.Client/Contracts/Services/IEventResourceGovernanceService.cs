using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IEventResourceGovernanceService
{
    Task<HalResourceOfSettingGroupResponseDto> GetAsync(bool instanceScope, CancellationToken cancellationToken = default);
    Task<BatchUpdateResponseDto> UpdateAsync(bool instanceScope, UpdateSettingBatchDto batch, CancellationToken cancellationToken = default);
}
