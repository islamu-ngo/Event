using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionCustomProperty.Validators;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public class SetEventSessionCustomPropertyValueCommandHandler : ICommandHandler<SetEventSessionCustomPropertyValueCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<SetEventSessionCustomPropertyValueCommand> _authorization;
    private readonly ILogger<SetEventSessionCustomPropertyValueCommandHandler> _logger;

    public SetEventSessionCustomPropertyValueCommandHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        ISerializableAttemptOwner owner,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<SetEventSessionCustomPropertyValueCommand> authorization,
        ILogger<SetEventSessionCustomPropertyValueCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
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

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(SetEventSessionCustomPropertyValueCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validator = new SetEventSessionCustomPropertyValueDtoValidator();
        var validationResult = await validator.ValidateAsync(request.ValueDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session custom property value set failed.");
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
        SetEventSessionCustomPropertyValueCommand request, Guid tenantId, Guid valueId, DateTime changedAt,
        CancellationToken cancellationToken)
    {
        var held = await _sessionCustomPropertyRepository.GetTrackedDefinitionWithOptions(
            request.ValueDto.EventSessionCustomPropertyDefinitionId, cancellationToken);
        if (held is null || held.TenantId != tenantId || held.EventSessionId != request.ValueDto.EventSessionId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event session custom property definition was not found for the requested session."],
                "Event session custom property value set failed.");
        }
        var definition = await _sessionCustomPropertyRepository.GetDefinitionWithDetails(held.Id, cancellationToken)
            ?? throw new InvalidOperationException("The fenced definition is unavailable.");

        var runtimeValidationErrors = CustomPropertyRuntimeValueValidator.ValidateSingle(definition, request.ValueDto);
        if (runtimeValidationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                runtimeValidationErrors,
                "Event session custom property value set failed.");
        }

        var dto = request.ValueDto;
        var value = new EventSessionCustomPropertyValue
        {
            Id = valueId,
            CreatedAt = changedAt,
            UpdatedAt = changedAt,
            EventSessionCustomPropertyDefinitionId = dto.EventSessionCustomPropertyDefinitionId,
            EventSessionId = dto.EventSessionId,
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

        var persisted = await _sessionCustomPropertyRepository.SetValue(value, cancellationToken);
        await _projectionUpdater.UpdateForValueAsync(persisted.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return BaseCommandResponse.Success(persisted.Id, "Event session custom property value set successfully.");
    }
}
