using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventCustomProperty.Validators;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class SetEventCustomPropertyValueCommandHandler : IRequestHandler<SetEventCustomPropertyValueCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public SetEventCustomPropertyValueCommandHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(SetEventCustomPropertyValueCommand request, CancellationToken cancellationToken)
    {
        var validator = new SetEventCustomPropertyValueDtoValidator();
        var validationResult = await validator.ValidateAsync(request.ValueDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event custom property value set failed.");
        }

        var definition = await _eventCustomPropertyRepository.GetDefinitionWithDetails(request.ValueDto.EventCustomPropertyDefinitionId);
        if (definition is null || definition.EventId != request.ValueDto.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event custom property definition was not found for the requested event."],
                "Event custom property value set failed.");
        }

        var runtimeValidationErrors = CustomPropertyRuntimeValueValidator.ValidateSingle(definition, request.ValueDto);
        if (runtimeValidationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                runtimeValidationErrors,
                "Event custom property value set failed.");
        }

        var value = _mapper.Map<EventCustomPropertyValue>(request.ValueDto);
        value.TenantId = _tenantContext.TenantId;
        value.CreatedBy = _currentUserService.UserId;
        value.UpdatedBy = _currentUserService.UserId;

        var persisted = await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var saved = await _eventCustomPropertyRepository.SetValue(value, ct);
                await _projectionUpdater.UpdateForValueAsync(saved.Id, ct);
                return saved;
            },
            cancellationToken);

        return BaseCommandResponse.Success(persisted.Id, "Event custom property value set successfully.");
    }
}
