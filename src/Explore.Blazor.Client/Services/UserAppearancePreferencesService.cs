using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public sealed class UserAppearancePreferencesService : IUserAppearancePreferencesService
{
    private readonly IUserAppearanceClient _apiClient;
    private readonly ILogger<UserAppearancePreferencesService> _logger;

    public UserAppearancePreferencesService(IUserAppearanceClient apiClient, ILogger<UserAppearancePreferencesService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResolvedAppearanceDto> GetCurrentPreferencesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _apiClient.GetCurrentUserAppearancePreferencesAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user appearance preferences");
            throw;
        }
    }

    public async Task SetActiveProfileAsync(SetActiveProfileRequestDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        try
        {
            await _apiClient.SetActiveAppearanceProfileAsync(dto, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user appearance preferences");
            throw;
        }
    }

}
