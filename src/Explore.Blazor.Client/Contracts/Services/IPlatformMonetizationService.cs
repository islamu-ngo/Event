using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IPlatformMonetizationService
{
    Task<HalResourceOfPlatformMonetizationSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateAsync(UpdatePlatformMonetizationSettingsDto request, CancellationToken cancellationToken = default);
}
