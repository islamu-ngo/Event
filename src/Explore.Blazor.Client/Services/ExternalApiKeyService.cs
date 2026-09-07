using System.Text.Json;
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

    /// <summary>Creates a new API key. Response includes the one-time secret.</summary>
    Task<CreateExternalApiKeyCommandResponse?> CreateApiKeyAsync(CreateExternalApiKeyDto dto);

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
    public async Task<CreateExternalApiKeyCommandResponse?> CreateApiKeyAsync(CreateExternalApiKeyDto dto)
    {
        try
        {
            return await _apiClient.CreateExternalApiKeyAsync(dto);
        }
        catch (ApiException ex) when (ex.StatusCode == 400)
        {
            // Controller returns BadRequest(CreateExternalApiKeyCommandResponse) but NSwag
            // deserializes 400 as ProblemDetails — actual validation errors are lost.
            // Parse the raw response body to surface them.
            _logger.LogWarning(ex,
                "[ExternalApiKeyService.CreateApiKeyAsync] Validation error. StatusCode: {StatusCode}, Body: {Body}",
                ex.StatusCode, ex.Response);
            return ParseCommandResponse(ex.Response);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[ExternalApiKeyService.CreateApiKeyAsync] API error. StatusCode: {StatusCode}", ex.StatusCode);
            throw;
        }
    }

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private static CreateExternalApiKeyCommandResponse ParseCommandResponse(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return new CreateExternalApiKeyCommandResponse { Success = false, Message = "Request failed with no details." };

        try
        {
            var parsed = JsonSerializer.Deserialize<CreateExternalApiKeyCommandResponse>(responseBody, CaseInsensitiveJson);
            if (parsed is not null)
                return parsed;
        }
        catch
        {
            // Response body isn't BaseCommandResponse shape — return raw text as message
        }

        return new CreateExternalApiKeyCommandResponse { Success = false, Message = responseBody };
    }

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
