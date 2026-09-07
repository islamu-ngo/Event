using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Organizations;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Service for managing tenant navigation links.
/// Communicates through the generated API client to retrieve, create, update, delete, and reorder navigation links.
/// </summary>
public class TenantNavigationService : ITenantNavigationService
{
    private readonly ITenantClient _apiClient;
    private readonly ILogger<TenantNavigationService> _logger;

    public TenantNavigationService(
        ITenantClient apiClient,
        ILogger<TenantNavigationService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves all navigation links for the current tenant.
    /// </summary>
    public async Task<ICollection<TenantNavigationLinkDto>> GetNavigationLinksAsync()
    {
        try
        {
            return await _apiClient.GetTenantNavigationLinksAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TENANT NAVIGATION SERVICE] Error fetching navigation links");
            return new List<TenantNavigationLinkDto>();
        }
    }

    /// <summary>
    /// Creates a new navigation link for the tenant.
    /// </summary>
    public async Task<BaseCommandResponseOfGuid?> CreateNavigationLinkAsync(CreateTenantNavigationLinkDto dto)
    {
        try
        {
            return await _apiClient.CreateTenantNavigationLinkAsync(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TENANT NAVIGATION SERVICE] Error creating navigation link");
            return CreateGuidFailure(ex);
        }
    }

    /// <summary>
    /// Updates an existing navigation link.
    /// </summary>
    public async Task<BaseCommandResponseOfboolean?> UpdateNavigationLinkAsync(Guid id, UpdateTenantNavigationLinkDto dto)
    {
        try
        {
            return await _apiClient.UpdateTenantNavigationLinkAsync(id, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TENANT NAVIGATION SERVICE] Error updating navigation link {Id}", id);
            return CreateBooleanFailure(ex);
        }
    }

    /// <summary>
    /// Deletes a navigation link.
    /// </summary>
    public async Task<BaseCommandResponseOfboolean?> DeleteNavigationLinkAsync(Guid id)
    {
        try
        {
            return await _apiClient.DeleteTenantNavigationLinkAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TENANT NAVIGATION SERVICE] Error deleting navigation link {Id}", id);
            return CreateBooleanFailure(ex);
        }
    }

    /// <summary>
    /// Reorders multiple navigation links.
    /// </summary>
    public async Task<BaseCommandResponseOfboolean?> ReorderNavigationLinksAsync(List<UpdateTenantNavigationLinkOrderDto> orders)
    {
        try
        {
            return await _apiClient.ReorderTenantNavigationLinksAsync(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TENANT NAVIGATION SERVICE] Error reordering navigation links");
            return CreateBooleanFailure(ex);
        }
    }

    private static BaseCommandResponseOfGuid CreateGuidFailure(Exception exception) =>
        new()
        {
            Success = false,
            Message = $"Error: {exception.Message}",
            Errors = [exception.Message]
        };

    private static BaseCommandResponseOfboolean CreateBooleanFailure(Exception exception) =>
        new()
        {
            Success = false,
            Message = $"Error: {exception.Message}",
            Errors = [exception.Message]
        };
}
