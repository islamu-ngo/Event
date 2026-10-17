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
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class SetEventCustomPropertyValueCommandHandler : ICommandHandler<SetEventCustomPropertyValueCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<SetEventCustomPropertyValueCommand> _authorization;
    private readonly ILogger<SetEventCustomPropertyValueCommandHandler> _logger;

    public SetEventCustomPropertyValueCommandHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        ISerializableAttemptOwner owner,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<SetEventCustomPropertyValueCommand> authorization,
        ILogger<SetEventCustomPropertyValueCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _owner = owner;
        _preparation = preparation;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _adminContext = adminContext;
        _tenantAuthority = tenantAuthority;
        _instanceAuthority = instanceAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(SetEventCustomPropertyValueCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validator = new SetEventCustomPropertyValueDtoValidator();
        var validationResult = await validator.ValidateAsync(request.ValueDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event custom property value set failed.");
        }

        Guid valueId = Guid.CreateVersion7();
        DateTime changedAt = DateTime.UtcNow;
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return BaseCommandResponse.Authorization<Guid>();
            return await SetAuthorizedAsync(request, tenantId, valueId, changedAt, token);
        }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> SetAuthorizedAsync(
        SetEventCustomPropertyValueCommand request, Guid tenantId, Guid valueId, DateTime changedAt,
        CancellationToken cancellationToken)
    {
        var held = await _eventCustomPropertyRepository.GetTrackedDefinitionWithOptions(
            request.ValueDto.EventCustomPropertyDefinitionId, cancellationToken);
        if (held is null || held.TenantId != tenantId || held.EventId != request.ValueDto.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event custom property definition was not found for the requested event."],
                "Event custom property value set failed.");
        }
        var definition = await _eventCustomPropertyRepository.GetDefinitionWithDetails(held.Id, cancellationToken)
            ?? throw new InvalidOperationException("The fenced definition is unavailable.");

        var runtimeValidationErrors = CustomPropertyRuntimeValueValidator.ValidateSingle(definition, request.ValueDto);
        if (runtimeValidationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                runtimeValidationErrors,
                "Event custom property value set failed.");
        }

        var dto = request.ValueDto;
        var value = new EventCustomPropertyValue
        {
            Id = valueId,
            CreatedAt = changedAt,
            UpdatedAt = changedAt,
            EventCustomPropertyDefinitionId = dto.EventCustomPropertyDefinitionId,
            EventId = dto.EventId,
            Ordinal = dto.Ordinal,
            TextValue = dto.TextValue,
            NumberValue = dto.NumberValue,
            BooleanValue = dto.BooleanValue,
            DateTimeValue = dto.DateTimeValue,
            OptionId = dto.OptionId,
            TenantId = tenantId,
            CreatedBy = _currentUserService.UserId,
            UpdatedBy = _currentUserService.UserId
        };

        var persisted = await _eventCustomPropertyRepository.SetValue(value, cancellationToken);
        await _projectionUpdater.UpdateForValueAsync(persisted.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return BaseCommandResponse.Success(persisted.Id, "Event custom property value set successfully.");
    }
}
