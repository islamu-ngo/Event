using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventSessionTemplate.Validators;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Commands;

public class CreateEventSessionTemplateCommandHandler : IRequestHandler<CreateEventSessionTemplateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public CreateEventSessionTemplateCommandHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IMapper mapper,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventSessionTemplateCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventSessionTemplateDtoValidator();
        var validationResult = await validator.ValidateAsync(request.SessionTemplateDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session template creation failed.");
        }

        if (await _sessionTemplateRepository.ExistsSessionTemplateKey(
                request.SessionTemplateDto.EventTemplateId,
                request.SessionTemplateDto.SessionTemplateKey,
                request.SessionTemplateDto.Version))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A session template with the same SessionTemplateKey and Version already exists for this event template."],
                "Event session template creation failed.");
        }

        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
            _tenantContext.TenantId,
            cancellationToken);
        if (request.SessionTemplateDto.Definitions.Count > maxDefinitions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event session template creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
                    maxDefinitions,
                    null,
                    request.SessionTemplateDto.Definitions.Count,
                    "event_session_template_definitions",
                    _tenantContext.TenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            _tenantContext.TenantId,
            cancellationToken);
        var overOptionDefinition = request.SessionTemplateDto.Definitions
            .FirstOrDefault(definition => definition.Options.Count > maxOptions);
        if (overOptionDefinition is not null)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event session template creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    null,
                    overOptionDefinition.Options.Count,
                    "event_session_template_definition_options",
                    _tenantContext.TenantId));
        }

        var definitionsResult = BuildDefinitionEntities(request.SessionTemplateDto.Definitions);
        if (definitionsResult.Errors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                definitionsResult.Errors,
                "Event session template creation failed.");
        }

        var sessionTemplate = _mapper.Map<EventSessionTemplate>(request.SessionTemplateDto);
        sessionTemplate.TenantId = _tenantContext.TenantId;
        sessionTemplate.CreatedBy = _currentUserService.UserId;
        sessionTemplate.UpdatedBy = _currentUserService.UserId;

        sessionTemplate = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _sessionTemplateRepository.CreateWithDefinitions(sessionTemplate, definitionsResult.Definitions, ct),
            cancellationToken);

        await _cache.RemoveAsync(
            GetListCacheKey(request.SessionTemplateDto.EventTemplateId, 1, PaginatedResult<object>.DefaultPageSize),
            cancellationToken);

        return BaseCommandResponse.Success(sessionTemplate.Id, "Event session template created successfully.");
    }

    private (IReadOnlyCollection<SessionTemplateDefinitionWithOptions> Definitions, List<string> Errors) BuildDefinitionEntities(
        IReadOnlyList<CreateEventSessionTemplateDefinitionDto> definitionDtos)
    {
        var errors = new List<string>();
        var definitions = new List<SessionTemplateDefinitionWithOptions>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var defDto in definitionDtos)
        {
            var governance = _customPropertyGovernancePolicy.EvaluateDefinition(defDto.Namespace, defDto.Key);
            if (!governance.IsValid)
            {
                errors.AddRange(governance.Errors);
                continue;
            }

            var compositeKey = $"{governance.NormalizedNamespace}:{governance.NormalizedKey}";
            if (!seenKeys.Add(compositeKey))
            {
                errors.Add($"Duplicate definition key '{compositeKey}' within the same session template.");
                continue;
            }

            var definition = _mapper.Map<EventSessionTemplateCustomPropertyDefinition>(defDto);
            definition.TenantId = _tenantContext.TenantId;
            definition.Namespace = governance.NormalizedNamespace;
            definition.Key = governance.NormalizedKey;
            definition.CreatedBy = _currentUserService.UserId;
            definition.UpdatedBy = _currentUserService.UserId;

            var options = CreateOptionEntities(defDto.Options, definition.Id);
            var defaultOption = options.SingleOrDefault(x => x.IsDefault);

            definitions.Add(new SessionTemplateDefinitionWithOptions(definition, options, defaultOption?.Id));
        }

        return (definitions, errors);
    }

    private List<EventSessionTemplateCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<CreateEventSessionTemplateOptionDto> optionDtos,
        Guid definitionId)
    {
        return optionDtos
            .Select(optionDto => new EventSessionTemplateCustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                EventSessionTemplateCustomPropertyDefinitionId = definitionId,
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

    private static string GetListCacheKey(Guid eventTemplateId, int pageNumber, int pageSize)
    {
        return $"session-templates:list:{eventTemplateId}:{pageNumber}:{pageSize}";
    }
}
