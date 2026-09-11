using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.DTOs.EventTemplate.Validators;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTemplates.Handlers.Commands;

public class CreateEventTemplateCommandHandler : IRequestHandler<CreateEventTemplateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public CreateEventTemplateCommandHandler(
        IEventTemplateRepository eventTemplateRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _eventTemplateRepository = eventTemplateRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventTemplateCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventTemplateDtoValidator();
        var validationResult = await validator.ValidateAsync(request.TemplateDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event template creation failed.");
        }

        if (await _eventTemplateRepository.ExistsTemplateKey(
                _tenantContext.TenantId,
                request.TemplateDto.TemplateKey,
                request.TemplateDto.Version))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A template with the same TemplateKey and Version already exists for this tenant."],
                "Event template creation failed.");
        }

        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
            _tenantContext.TenantId,
            cancellationToken);
        if (request.TemplateDto.Definitions.Count > maxDefinitions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event template creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
                    maxDefinitions,
                    null,
                    request.TemplateDto.Definitions.Count,
                    "event_template_definitions",
                    _tenantContext.TenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            _tenantContext.TenantId,
            cancellationToken);
        var overOptionDefinition = request.TemplateDto.Definitions
            .FirstOrDefault(definition => definition.Options.Count > maxOptions);
        if (overOptionDefinition is not null)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event template creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    null,
                    overOptionDefinition.Options.Count,
                    "event_template_definition_options",
                    _tenantContext.TenantId));
        }

        var definitionsResult = BuildDefinitionEntities(request.TemplateDto.Definitions);
        if (definitionsResult.Errors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                definitionsResult.Errors,
                "Event template creation failed.");
        }

        var dto = request.TemplateDto;
        var template = new EventTemplate
        {
            TemplateKey = dto.TemplateKey,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            EventTypeId = dto.EventTypeId,
            Version = dto.Version,
            IsPublished = dto.IsPublished,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
            TenantId = _tenantContext.TenantId,
            CreatedBy = _currentUserService.UserId,
            UpdatedBy = _currentUserService.UserId
        };

        template = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _eventTemplateRepository.CreateWithDefinitions(template, definitionsResult.Definitions, ct),
            cancellationToken);

        await _cache.RemoveAsync(
            GetListCacheKey(_tenantContext.TenantId, null, 1, PaginatedResult<object>.DefaultPageSize),
            cancellationToken);

        return BaseCommandResponse.Success(template.Id, "Event template created successfully.");
    }

    private (IReadOnlyCollection<TemplateDefinitionWithOptions> Definitions, List<string> Errors) BuildDefinitionEntities(
        IReadOnlyList<CreateEventTemplateDefinitionDto> definitionDtos)
    {
        var errors = new List<string>();
        var definitions = new List<TemplateDefinitionWithOptions>();
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
                errors.Add($"Duplicate definition key '{compositeKey}' within the same template.");
                continue;
            }

            var definition = new EventTemplateCustomPropertyDefinition
            {
                Namespace = governance.NormalizedNamespace,
                Key = governance.NormalizedKey,
                DisplayName = defDto.DisplayName,
                Description = defDto.Description,
                PropertyType = defDto.PropertyType,
                IsRequired = defDto.IsRequired,
                IsMulti = defDto.IsMulti,
                IsActive = defDto.IsActive,
                SortOrder = defDto.SortOrder,
                ExposureLevel = defDto.ExposureLevel,
                IsSearchable = defDto.IsSearchable,
                IsFilterable = defDto.IsFilterable,
                IsExportable = defDto.IsExportable,
                IsModerationRelevant = defDto.IsModerationRelevant,
                IsAnalyticsRelevant = defDto.IsAnalyticsRelevant,
                IsSystemOwned = defDto.IsSystemOwned,
                DefaultTextValue = defDto.DefaultTextValue,
                DefaultNumberValue = defDto.DefaultNumberValue,
                DefaultBooleanValue = defDto.DefaultBooleanValue,
                DefaultDateTimeValue = defDto.DefaultDateTimeValue,
                MinLength = defDto.MinLength,
                MaxLength = defDto.MaxLength,
                RegexPattern = defDto.RegexPattern,
                MinNumber = defDto.MinNumber,
                MaxNumber = defDto.MaxNumber,
                MinDateTime = defDto.MinDateTime,
                MaxDateTime = defDto.MaxDateTime,
                AllowedUrlSchemes = defDto.AllowedUrlSchemes,
                TenantId = _tenantContext.TenantId,
                CreatedBy = _currentUserService.UserId,
                UpdatedBy = _currentUserService.UserId
            };

            var options = CreateOptionEntities(defDto.Options, definition.Id);
            var defaultOption = options.SingleOrDefault(x => x.IsDefault);

            definitions.Add(new TemplateDefinitionWithOptions(definition, options, defaultOption?.Id));
        }

        return (definitions, errors);
    }

    private List<EventTemplateCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<CreateEventTemplateOptionDto> optionDtos,
        Guid definitionId)
    {
        return optionDtos
            .Select(optionDto => new EventTemplateCustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                EventTemplateCustomPropertyDefinitionId = definitionId,
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

    private static string GetListCacheKey(Guid tenantId, int? eventTypeId, int pageNumber, int pageSize)
    {
        return $"event-templates:list:{tenantId}:{eventTypeId}:{pageNumber}:{pageSize}";
    }
}
