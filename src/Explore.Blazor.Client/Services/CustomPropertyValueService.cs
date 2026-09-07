using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.CustomProperties;
using Explore.Blazor.Client.Models.CustomProperties;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public sealed class CustomPropertyValueService : ICustomPropertyValueService
{
    private readonly IEventCustomPropertyClient _eventClient;
    private readonly IEventSessionCustomPropertyClient _sessionClient;
    private readonly ILogger<CustomPropertyValueService> _logger;

    public CustomPropertyValueService(
        IEventCustomPropertyClient eventClient,
        IEventSessionCustomPropertyClient sessionClient,
        ILogger<CustomPropertyValueService> logger)
    {
        _eventClient = eventClient ?? throw new ArgumentNullException(nameof(eventClient));
        _sessionClient = sessionClient ?? throw new ArgumentNullException(nameof(sessionClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<CustomPropertyValueModel>> GetEventValuesAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var response = await _eventClient.GetEventCustomPropertyValuesAsync(eventId, cancellationToken: cancellationToken);
        return response?.Select(CustomPropertyValueModel.FromEventDto).ToList() ?? new List<CustomPropertyValueModel>();
    }

    public async Task<BaseCommandResponseOfGuid?> SetEventValueAsync(Guid definitionId, Guid eventId, CustomPropertyValueModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new SetEventCustomPropertyValueDto
            {
                EventCustomPropertyDefinitionId = definitionId,
                EventId = eventId,
                Ordinal = model.Ordinal,
                TextValue = model.TextValue,
                NumberValue = model.NumberValue,
                BooleanValue = model.BooleanValue,
                DateTimeValue = model.DateTimeValue,
                OptionId = model.OptionId
            };

            return await _eventClient.SetEventCustomPropertyValueAsync(body, cancellationToken: cancellationToken);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            _logger.LogWarning(ex, "[CP VAL] API error setting value for Event {EventId} / Def {DefinitionId}: {Detail}", eventId, definitionId, ex.Result?.Detail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CP VAL] Unexpected error setting value for Event {EventId} / Def {DefinitionId}", eventId, definitionId);
            throw;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> SetEventMultiValuesAsync(Guid definitionId, Guid eventId, IEnumerable<CustomPropertyValueModel> models, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new SetEventCustomPropertyMultiValuesDto
            {
                DefinitionId = definitionId,
                EventId = eventId,
                Values = models.Select((m, i) => new SetEventCustomPropertyValueDto
                {
                    EventCustomPropertyDefinitionId = definitionId,
                    EventId = eventId,
                    Ordinal = i,
                    TextValue = m.TextValue,
                    NumberValue = m.NumberValue,
                    BooleanValue = m.BooleanValue,
                    DateTimeValue = m.DateTimeValue,
                    OptionId = m.OptionId
                }).ToList()
            };

            return await _eventClient.SetEventCustomPropertyMultiValuesAsync(body, cancellationToken: cancellationToken);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            _logger.LogWarning(ex, "[CP VAL] API error setting multi values for Event {EventId} / Def {DefinitionId}: {Detail}", eventId, definitionId, ex.Result?.Detail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CP VAL] Unexpected error setting multi values for Event {EventId} / Def {DefinitionId}", eventId, definitionId);
            throw;
        }
    }

    public async Task<IReadOnlyList<CustomPropertyValueModel>> GetEventSessionValuesAsync(Guid eventSessionId, CancellationToken cancellationToken = default)
    {
        var response = await _sessionClient.GetEventSessionCustomPropertyValuesAsync(eventSessionId, cancellationToken: cancellationToken);
        return response?.Select(CustomPropertyValueModel.FromEventSessionDto).ToList() ?? new List<CustomPropertyValueModel>();
    }

    public async Task<BaseCommandResponseOfGuid?> SetEventSessionValueAsync(Guid definitionId, Guid eventSessionId, CustomPropertyValueModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new SetEventSessionCustomPropertyValueDto
            {
                EventSessionCustomPropertyDefinitionId = definitionId,
                EventSessionId = eventSessionId,
                Ordinal = model.Ordinal,
                TextValue = model.TextValue,
                NumberValue = model.NumberValue,
                BooleanValue = model.BooleanValue,
                DateTimeValue = model.DateTimeValue,
                OptionId = model.OptionId
            };

            return await _sessionClient.SetEventSessionCustomPropertyValueAsync(body, cancellationToken: cancellationToken);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            _logger.LogWarning(ex, "[CP VAL] API error setting value for EventSession {EventSessionId} / Def {DefinitionId}: {Detail}", eventSessionId, definitionId, ex.Result?.Detail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CP VAL] Unexpected error setting value for EventSession {EventSessionId} / Def {DefinitionId}", eventSessionId, definitionId);
            throw;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> SetEventSessionMultiValuesAsync(Guid definitionId, Guid eventSessionId, IEnumerable<CustomPropertyValueModel> models, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new SetEventSessionCustomPropertyMultiValuesDto
            {
                DefinitionId = definitionId,
                EventSessionId = eventSessionId,
                Values = models.Select((m, i) => new SetEventSessionCustomPropertyValueDto
                {
                    EventSessionCustomPropertyDefinitionId = definitionId,
                    EventSessionId = eventSessionId,
                    Ordinal = i,
                    TextValue = m.TextValue,
                    NumberValue = m.NumberValue,
                    BooleanValue = m.BooleanValue,
                    DateTimeValue = m.DateTimeValue,
                    OptionId = m.OptionId
                }).ToList()
            };

            return await _sessionClient.SetEventSessionCustomPropertyMultiValuesAsync(body, cancellationToken: cancellationToken);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            _logger.LogWarning(ex, "[CP VAL] API error setting multi values for EventSession {EventSessionId} / Def {DefinitionId}: {Detail}", eventSessionId, definitionId, ex.Result?.Detail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CP VAL] Unexpected error setting multi values for EventSession {EventSessionId} / Def {DefinitionId}", eventSessionId, definitionId);
            throw;
        }
    }
}
