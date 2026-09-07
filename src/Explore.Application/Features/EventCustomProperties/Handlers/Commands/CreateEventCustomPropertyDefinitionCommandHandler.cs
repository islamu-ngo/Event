using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventCustomProperty.Validators;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class CreateEventCustomPropertyDefinitionCommandHandler : IRequestHandler<CreateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public CreateEventCustomPropertyDefinitionCommandHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IMapper mapper,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventCustomPropertyDefinitionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.DefinitionDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event custom property definition creation failed.");
        }

        var governance = _customPropertyGovernancePolicy.EvaluateDefinition(request.DefinitionDto.Namespace, request.DefinitionDto.Key);
        if (!governance.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                governance.Errors,
                "Event custom property definition creation failed.");
        }

        if (await _eventCustomPropertyRepository.ExistsDefinitionKey(
                request.DefinitionDto.EventId,
                governance.NormalizedNamespace,
                governance.NormalizedKey))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A custom property definition with the same Namespace + Key already exists for this event."],
                "Event custom property definition creation failed.");
        }

        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key,
            _tenantContext.TenantId,
            cancellationToken);
        var currentDefinitionCount = await _eventCustomPropertyRepository.CountDefinitionsForEvent(
            request.DefinitionDto.EventId,
            cancellationToken);
        if (currentDefinitionCount >= maxDefinitions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event custom property definition creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key,
                    maxDefinitions,
                    currentDefinitionCount,
                    currentDefinitionCount + 1,
                    "event_custom_property_definitions",
                    _tenantContext.TenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            _tenantContext.TenantId,
            cancellationToken);
        if (request.DefinitionDto.Options.Count > maxOptions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event custom property definition creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    null,
                    request.DefinitionDto.Options.Count,
                    "event_custom_property_options",
                    _tenantContext.TenantId));
        }

        var definition = _mapper.Map<EventCustomPropertyDefinition>(request.DefinitionDto);
        definition.TenantId = _tenantContext.TenantId;
        definition.Namespace = governance.NormalizedNamespace;
        definition.Key = governance.NormalizedKey;
        definition.InstantiatedAt = DateTimeOffset.UtcNow;
        definition.CreatedBy = _currentUserService.UserId;
        definition.UpdatedBy = _currentUserService.UserId;

        var options = CreateOptionEntities(request.DefinitionDto.Options, definition.Id);
        var defaultOption = options.SingleOrDefault(x => x.IsDefault);

        definition = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _eventCustomPropertyRepository.CreateWithOptions(definition, options, defaultOption?.Id, ct),
            cancellationToken);

        await _cache.RemoveAsync(
            GetListCacheKey(request.DefinitionDto.EventId, 1, PaginatedResult<object>.DefaultPageSize),
            cancellationToken);

        return BaseCommandResponse.Success(definition.Id, "Event custom property definition created successfully.");
    }

    private List<EventCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<DTOs.EventCustomProperty.CreateEventCustomPropertyOptionDto> optionDtos,
        Guid definitionId)
    {
        return optionDtos
            .Select(optionDto => new EventCustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                EventCustomPropertyDefinitionId = definitionId,
                Namespace = CustomPropertyIdentity.NormalizeNamespace(optionDto.Namespace),
                Key = CustomPropertyIdentity.NormalizeKey(optionDto.Key),
                DisplayName = optionDto.DisplayName,
                Description = optionDto.Description,
                Value = optionDto.Value,
                IsDefault = optionDto.IsDefault,
                IsActive = optionDto.IsActive,
                SortOrder = optionDto.SortOrder,
                CreatedBy = _currentUserService.UserId,
                UpdatedBy = _currentUserService.UserId,
            })
            .ToList();
    }

    private static string GetListCacheKey(Guid eventId, int pageNumber, int pageSize)
    {
        return $"event-custom-properties:list:{eventId}:{pageNumber}:{pageSize}";
    }
}
