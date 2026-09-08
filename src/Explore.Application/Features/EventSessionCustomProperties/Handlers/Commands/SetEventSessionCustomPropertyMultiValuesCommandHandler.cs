using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionCustomProperty.Validators;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public class SetEventSessionCustomPropertyMultiValuesCommandHandler : IRequestHandler<SetEventSessionCustomPropertyMultiValuesCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly IUnitOfWork _unitOfWork;

    public SetEventSessionCustomPropertyMultiValuesCommandHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IMapper mapper,
        IUnitOfWork unitOfWork)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(SetEventSessionCustomPropertyMultiValuesCommand request, CancellationToken cancellationToken)
    {
        var validator = new SetEventSessionCustomPropertyValueDtoValidator();
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
                "Event session custom property multi-value set failed.");
        }

        var definition = await _sessionCustomPropertyRepository.GetDefinitionWithDetails(request.DefinitionId);
        if (definition is null || definition.EventSessionId != request.EventSessionId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event session custom property definition was not found for the requested session."],
                "Event session custom property multi-value set failed.");
        }

        var maxRows = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxMultiValueRowsPerValue.Key,
            definition.TenantId,
            cancellationToken);

        if (request.Values.Count > maxRows)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event session custom property multi-value set failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxMultiValueRowsPerValue.Key,
                    maxRows,
                    null,
                    request.Values.Count,
                    "event_session_custom_property_multi_values",
                    definition.TenantId));
        }

        var runtimeValidationErrors = CustomPropertyRuntimeValueValidator.ValidateMany(definition, request.Values);
        if (runtimeValidationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                runtimeValidationErrors,
                "Event session custom property multi-value set failed.");
        }

        var values = request.Values
            .Select((dto, index) =>
            {
                var value = _mapper.Map<EventSessionCustomPropertyValue>(dto);
                value.EventSessionCustomPropertyDefinitionId = request.DefinitionId;
                value.EventSessionId = request.EventSessionId;
                value.TenantId = _tenantContext.TenantId;
                value.Ordinal = index;
                value.CreatedBy = _currentUserService.UserId;
                value.UpdatedBy = _currentUserService.UserId;
                return value;
            })
            .ToList();

        await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await _sessionCustomPropertyRepository.SetMultiValues(request.DefinitionId, request.EventSessionId, values, ct);
                await _projectionUpdater.UpdateForDefinitionAsync(request.DefinitionId, ct);
            },
            cancellationToken);

        return BaseCommandResponse.Success(request.DefinitionId, "Event session custom property values set successfully.");
    }
}
