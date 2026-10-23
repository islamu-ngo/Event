using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventCustomProperty.Validators;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class SetEventCustomPropertyMultiValuesCommandHandler : ICommandHandler<SetEventCustomPropertyMultiValuesCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<SetEventCustomPropertyMultiValuesCommand> _authorization;
    private readonly ILogger<SetEventCustomPropertyMultiValuesCommandHandler> _logger;

    public SetEventCustomPropertyMultiValuesCommandHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        ISerializableAttemptOwner owner,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<SetEventCustomPropertyMultiValuesCommand> authorization,
        ILogger<SetEventCustomPropertyMultiValuesCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _owner = owner;
        _preparation = preparation;
        _adminContext = adminContext;
        _tenantAuthority = tenantAuthority;
        _instanceAuthority = instanceAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(SetEventCustomPropertyMultiValuesCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validator = new SetEventCustomPropertyValueDtoValidator();
        var errors = new List<string>();
        for (var i = 0; i < request.Values.Count; i++)
        {
            var validationResult = await validator.ValidateAsync(request.Values[i], cancellationToken);
            if (!validationResult.IsValid)
            {
                errors.AddRange(validationResult.Errors.Select(e => $"Value[{i}]: {e.ErrorMessage}"));
            }
        }

        if (errors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                errors,
                "Event custom property multi-value set failed.");
        }

        Guid[] valueIds = request.Values.Select(_ => Guid.CreateVersion7()).ToArray();
        DateTime createdAt = DateTime.UtcNow;
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return BaseCommandResponse.Authorization<Guid>();
            return await SetAuthorizedAsync(request, tenantId, valueIds, createdAt, token);
        }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> SetAuthorizedAsync(
        SetEventCustomPropertyMultiValuesCommand request, Guid tenantId, Guid[] valueIds, DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var held = await _eventCustomPropertyRepository.GetTrackedDefinitionWithOptions(
            request.DefinitionId, cancellationToken);
        if (held is null || held.TenantId != tenantId || held.EventId != request.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event custom property definition was not found for the requested event."],
                "Event custom property multi-value set failed.");
        }
        var definition = await _eventCustomPropertyRepository.GetDefinitionWithDetails(held.Id, cancellationToken)
            ?? throw new InvalidOperationException("The fenced definition is unavailable.");

        var maxRows = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxMultiValueRowsPerValue.Key,
            definition.TenantId,
            cancellationToken);

        if (request.Values.Count > maxRows)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event custom property multi-value set failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxMultiValueRowsPerValue.Key,
                    maxRows,
                    null,
                    request.Values.Count,
                    "event_custom_property_multi_values",
                    definition.TenantId));
        }

        var runtimeValidationErrors = CustomPropertyRuntimeValueValidator.ValidateMany(definition, request.Values);
        if (runtimeValidationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                runtimeValidationErrors,
                "Event custom property multi-value set failed.");
        }

        var values = request.Values
            .Select((dto, index) => new EventCustomPropertyValue
            {
                Id = valueIds[index],
                CreatedAt = createdAt,
                EventCustomPropertyDefinitionId = request.DefinitionId,
                EventId = request.EventId,
                Ordinal = index,
                TextValue = dto.TextValue,
                NumberValue = dto.NumberValue,
                BooleanValue = dto.BooleanValue,
                DateTimeValue = dto.DateTimeValue,
                OptionId = dto.OptionId,
                TenantId = tenantId,
                CreatedBy = _currentUserService.UserId,
                UpdatedBy = _currentUserService.UserId
            })
            .ToList();

        await _eventCustomPropertyRepository.SetMultiValues(
            request.DefinitionId, request.EventId, values, cancellationToken);
        await _projectionUpdater.UpdateForDefinitionAsync(request.DefinitionId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return BaseCommandResponse.Success(request.DefinitionId, "Event custom property values set successfully.");
    }
}
