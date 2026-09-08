using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.EventSessionTemplates;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public sealed class EventSessionTemplateService : IEventSessionTemplateService
{
    private readonly IEventSessionTemplateClient _apiClient;
    private readonly ILogger<EventSessionTemplateService> _logger;

    public EventSessionTemplateService(IEventSessionTemplateClient apiClient, ILogger<EventSessionTemplateService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HalCollectionResourceOfEventSessionTemplateListDto> GetTemplatesAsync(
        Guid? eventTemplateId = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        try
        {
            return await _apiClient.GetEventSessionTemplatesAsync(
                eventTemplateId,
                pageNumber,
                pageSize,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EVENT SESSION TEMPLATE] Failed to fetch templates for event template {EventTemplateId}", eventTemplateId);
            return new HalCollectionResourceOfEventSessionTemplateListDto
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = 0,
                TotalPages = 0,
                _embedded = new HalCollectionEmbeddedOfEventSessionTemplateListDto { Items = [] }
            };
        }
    }

    public async Task<HalResourceOfEventSessionTemplateDto?> GetTemplateByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        try
        {
            return await _apiClient.GetEventSessionTemplateByIdAsync(id, cancellationToken: ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EVENT SESSION TEMPLATE] Failed to fetch template {SessionTemplateId}", id);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateTemplateAsync(
        CreateEventSessionTemplateDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        try
        {
            return await _apiClient.CreateEventSessionTemplateAsync(dto, cancellationToken: ct);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            _logger.LogWarning(ex, "[EVENT SESSION TEMPLATE] API error creating template: {Detail}", ex.Result?.Detail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EVENT SESSION TEMPLATE] Unexpected error creating template");
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateTemplateAsync(
        Guid id,
        Guid expectedConcurrencyStamp,
        UpdateEventSessionTemplateDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        try
        {
            return await _apiClient.UpdateEventSessionTemplateAsync(id, $"\"{expectedConcurrencyStamp}\"", dto, cancellationToken: ct);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            _logger.LogWarning(ex, "[EVENT SESSION TEMPLATE] API error updating template {SessionTemplateId}: {Detail}", id, ex.Result?.Detail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EVENT SESSION TEMPLATE] Unexpected error updating template {SessionTemplateId}", id);
            return null;
        }
    }

    public async Task<bool> DeleteTemplateAsync(
        Guid id,
        CancellationToken ct = default)
    {
        try
        {
            await _apiClient.DeleteEventSessionTemplateAsync(id, cancellationToken: ct);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EVENT SESSION TEMPLATE] Unexpected error deleting template {SessionTemplateId}", id);
            return false;
        }
    }
}
