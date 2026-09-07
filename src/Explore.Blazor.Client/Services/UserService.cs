using System.Net.Http.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Service for managing user-related operations.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Synchronizes the current authenticated user with the backend.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> SyncUserAsync();

    /// <summary>
    /// Gets the current authenticated user's details.
    /// </summary>
    Task<UserDto?> GetCurrentUserAsync();

    /// <summary>
    /// Gets the current authenticated user's DB-backed admin authority.
    /// </summary>
    Task<AdminAuthorityDto?> GetAdminAuthorityAsync();

    /// <summary>
    /// Updates the current user's profile.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> UpdateUserAsync(Guid userId, Guid expectedConcurrencyStamp, UpdateUserDto userDto);

    /// <summary>
    /// Deletes the current user's account.
    /// </summary>
    Task<PrivacyErasureStartDto?> DeleteUserAsync();

    /// <summary>
    /// Resolves the appropriate tenant redirection target for the current authenticated user.
    /// </summary>
    Task<UserTenantRedirectionDto?> ResolveUserTenantRedirectionAsync();

    /// <summary>
    /// Updates the user's last active tenant setting in the database.
    /// </summary>
    Task<bool> UpdateUserLastActiveTenantAsync(Guid tenantId);
}

/// <summary>
/// Implementation of user service using the Event API client.
/// </summary>
public class UserService : IUserService
{
    private readonly IUserClient _apiClient;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserClient apiClient, ILogger<UserService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> SyncUserAsync()
    {
        try
        {
            _logger.LogInformation("Syncing user");
            var response = await _apiClient.SyncUserAsync();
            _logger.LogInformation("Sync result: Success={Success}, Id={Id}", response?.Success, response?.Id);
            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 200)
        {
            // NSwag sometimes throws when response body doesn't match expected schema
            // but the operation was successful (status 200)
            _logger.LogWarning(ex, "Sync completed with status 200 but response parsing issue");
            return new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "User synced successfully"
            };
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error syncing user: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = $"API error: {ex.Message}",
                Errors = new List<string> { ex.Response ?? ex.Message }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing user");
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <inheritdoc />
    public async Task<UserDto?> GetCurrentUserAsync()
    {
        try
        {
            _logger.LogInformation("Fetching current user");
            var user = (await _apiClient.GetCurrentUserAsync()).ToDto();
            _logger.LogInformation("User found: {Email}", user?.Email);
            return user;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("User not found (404) - attempting auto-sync");

            // Auto-sync user if not found
            try
            {
                var syncResult = await SyncUserAsync();
                if (syncResult?.Success == true)
                {
                    _logger.LogInformation("Auto-sync successful, retrying GetCurrentUser");
                    await Task.Delay(100); // Wait for DB write to complete
                    return (await _apiClient.GetCurrentUserAsync()).ToDto();
                }
                else
                {
                    _logger.LogWarning("Auto-sync failed: {Message}", syncResult?.Message);
                }
            }
            catch (Exception syncEx)
            {
                _logger.LogError(syncEx, "Auto-sync exception");
            }

            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            _logger.LogWarning("User not authenticated (401)");
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<AdminAuthorityDto?> GetAdminAuthorityAsync()
    {
        try
        {
            return await _apiClient.GetCurrentUserAdminAuthorityAsync();
        }
        catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
            _logger.LogWarning("User is not authorized to fetch admin authority: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error fetching admin authority: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching admin authority");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> UpdateUserAsync(Guid userId, Guid expectedConcurrencyStamp, UpdateUserDto userDto)
    {
        try
        {
            _logger.LogInformation("Updating user");
            return await _apiClient.UpdateCurrentUserAsync(userId, userDto, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error updating user: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PrivacyErasureStartDto?> DeleteUserAsync()
    {
        try
        {
            _logger.LogInformation("Deleting user");
            return await _apiClient.DeleteCurrentUserAsync(Guid.CreateVersion7().ToString("D"));
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error deleting user: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UserTenantRedirectionDto?> ResolveUserTenantRedirectionAsync()
    {
        try
        {
            _logger.LogInformation("Resolving user tenant redirection");
            return await _apiClient.ResolveUserTenantRedirectionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving user tenant redirection");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateUserLastActiveTenantAsync(Guid tenantId)
    {
        try
        {
            _logger.LogInformation("Updating user last active tenant to {TenantId}", tenantId);
            return await _apiClient.UpdateUserLastActiveTenantAsync(tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user last active tenant");
            return false;
        }
    }
}
