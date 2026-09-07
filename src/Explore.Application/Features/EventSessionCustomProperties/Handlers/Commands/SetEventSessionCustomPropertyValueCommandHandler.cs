using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionCustomProperty.Validators;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public class SetEventSessionCustomPropertyValueCommandHandler : IRequestHandler<SetEventSessionCustomPropertyValueCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public SetEventSessionCustomPropertyValueCommandHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(SetEventSessionCustomPropertyValueCommand request, CancellationToken cancellationToken)
    {
        var validator = new SetEventSessionCustomPropertyValueDtoValidator();
        var validationResult = await validator.ValidateAsync(request.ValueDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session custom property value set failed.");
        }

        var definition = await _sessionCustomPropertyRepository.GetDefinitionWithDetails(request.ValueDto.EventSessionCustomPropertyDefinitionId);
        if (definition is null || definition.EventSessionId != request.ValueDto.EventSessionId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event session custom property definition was not found for the requested session."],
                "Event session custom property value set failed.");
        }

        var runtimeValidationErrors = CustomPropertyRuntimeValueValidator.ValidateSingle(definition, request.ValueDto);
        if (runtimeValidationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                runtimeValidationErrors,
                "Event session custom property value set failed.");
        }

        var value = _mapper.Map<EventSessionCustomPropertyValue>(request.ValueDto);
        value.TenantId = _tenantContext.TenantId;
        value.CreatedBy = _currentUserService.UserId;
        value.UpdatedBy = _currentUserService.UserId;

        var persisted = await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var saved = await _sessionCustomPropertyRepository.SetValue(value, ct);
                await _projectionUpdater.UpdateForValueAsync(saved.Id, ct);
                return saved;
            },
            cancellationToken);

        return BaseCommandResponse.Success(persisted.Id, "Event session custom property value set successfully.");
    }
}
