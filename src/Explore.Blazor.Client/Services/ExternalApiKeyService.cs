using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Manages external API key CRUD operations for User, Organization, Group, Tenant, and InstanceAdmin owner types.
/// </summary>
public interface IExternalApiKeyService
{
    /// <summary>Returns all API keys visible to the current caller.</summary>
    Task<ICollection<ExternalApiKeyListDto>> GetApiKeysAsync();

    /// <summary>Returns a single API key by ID.</summary>
    Task<ExternalApiKeyListDto?> GetApiKeyByIdAsync(Guid id);

    /// <summary>Creates or recovers one intended operation; only its acknowledged first creation discloses a secret.</summary>
    Task<CreateExternalApiKeyCommandResponse?> CreateApiKeyAsync(CreateExternalApiKeyDto dto, string operationKey);

    /// <summary>Updates the name, scopes, or expiry of an existing key.</summary>
    Task<BaseCommandResponseOfGuid?> UpdateApiKeyPolicyAsync(Guid id, UpdateExternalApiKeyPolicyDto dto);

    /// <summary>Revokes an API key permanently.</summary>
    Task RevokeApiKeyAsync(Guid id);
}

public class ExternalApiKeyService : IExternalApiKeyService
{
    private readonly IExternalApiKeyClient _apiClient;
    private readonly ILogger<ExternalApiKeyService> _logger;

    public ExternalApiKeyService(IExternalApiKeyClient apiClient, ILogger<ExternalApiKeyService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ICollection<ExternalApiKeyListDto>> GetApiKeysAsync()
    {
        try
        {
            return await _apiClient.GetExternalApiKeysAsync() ?? [];
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[ExternalApiKeyService.GetApiKeysAsync] API error. StatusCode: {StatusCode}", ex.StatusCode);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ExternalApiKeyService.GetApiKeysAsync] Unexpected error");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<ExternalApiKeyListDto?> GetApiKeyByIdAsync(Guid id)
    {
        try
        {
            return await _apiClient.GetExternalApiKeyByIdAsync(id);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[ExternalApiKeyService.GetApiKeyByIdAsync] API error for {Id}. StatusCode: {StatusCode}", id, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ExternalApiKeyService.GetApiKeyByIdAsync] Unexpected error for {Id}", id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CreateExternalApiKeyCommandResponse?> CreateApiKeyAsync(CreateExternalApiKeyDto dto, string operationKey)
    {
        try
        {
            var response = await _apiClient.CreateExternalApiKeyAsync(operationKey, dto);
            return response?.Success == true ? response : CreationFailure();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("API key creation failed. StatusCode: {StatusCode}", ex.StatusCode);
            return CreationFailure(ex.StatusCode);
        }
        catch (Exception)
        {
            _logger.LogWarning("API key creation ended without an acknowledged response.");
            return CreationFailure();
        }
    }

    private static CreateExternalApiKeyCommandResponse CreationFailure(int? statusCode = null) =>
        new()
        {
            Success = false,
            Message = statusCode switch
            {
                400 => "The request was rejected. Cancel and correct the key policy.",
                401 => "Sign in again before retrying this operation.",
                403 => "Your current access does not permit this operation. Cancel or restore access before retrying.",
                404 => "The key or owner is unavailable. Cancel and review your keys before issuing a replacement.",
                409 => "This operation conflicts with an earlier request. Cancel and review your keys before starting a new operation.",
                _ => "The API key request did not complete. Retry this operation to check its outcome."
            }
        };

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> UpdateApiKeyPolicyAsync(Guid id, UpdateExternalApiKeyPolicyDto dto)
    {
        try
        {
            return await _apiClient.UpdateExternalApiKeyAsync(id, dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[ExternalApiKeyService.UpdateApiKeyPolicyAsync] API error for {Id}. StatusCode: {StatusCode}", id, ex.StatusCode);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RevokeApiKeyAsync(Guid id)
    {
        try
        {
            await _apiClient.DeleteExternalApiKeyAsync(id);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[ExternalApiKeyService.RevokeApiKeyAsync] API error for {Id}. StatusCode: {StatusCode}", id, ex.StatusCode);
            throw;
        }
    }
}
