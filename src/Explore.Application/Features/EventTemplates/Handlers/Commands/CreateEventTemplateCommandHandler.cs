using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
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
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventTemplates.Handlers.Commands;

public class CreateEventTemplateCommandHandler : ICommandHandler<CreateEventTemplateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly HybridCache _cache;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<CreateEventTemplateCommand> _authorization;
    private readonly ILogger<CreateEventTemplateCommandHandler> _logger;
    private readonly IHumanAdministrativeActionPreparation _preparation;

    public CreateEventTemplateCommandHandler(
        IEventTemplateRepository eventTemplateRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        HybridCache cache,
        ISerializableAttemptOwner owner,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<CreateEventTemplateCommand> authorization,
        ILogger<CreateEventTemplateCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _eventTemplateRepository = eventTemplateRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _cache = cache;
        _owner = owner;
        _adminContext = adminContext;
        _tenantAuthority = tenantAuthority;
        _instanceAuthority = instanceAuthority;
        _authorization = authorization;
        _logger = logger;
        _preparation = preparation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventTemplateCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        request = request with { TemplateDto = request.TemplateDto with { } };
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validationResult = await new CreateEventTemplateDtoValidator()
            .ValidateAsync(request.TemplateDto, cancellationToken);
        if (!validationResult.IsValid)
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage), "Event template creation failed.");
        Guid templateId = Guid.CreateVersion7();
        DateTime createdAt = DateTime.UtcNow;
        var definitionIds = request.TemplateDto.Definitions.Select(definition =>
            (Id: Guid.CreateVersion7(), OptionIds: definition.Options.Select(_ => Guid.CreateVersion7()).ToArray())).ToArray();
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        var response = await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return BaseCommandResponse.Authorization<Guid>();
            return await CreateAuthorizedAsync(request, tenantId, templateId, createdAt, definitionIds, token);
        }, cancellationToken);
        if (response.IsSuccess)
            await _cache.RemoveAsync(
                GetListCacheKey(tenantId, null, 1, PaginatedResult<object>.DefaultPageSize),
                cancellationToken);
        return response;
    }

    private async Task<BaseCommandResponse<Guid>> CreateAuthorizedAsync(
        CreateEventTemplateCommand request, Guid tenantId, Guid templateId, DateTime createdAt,
        (Guid Id, Guid[] OptionIds)[] definitionIds, CancellationToken cancellationToken)
    {
        if (await _eventTemplateRepository.ExistsTemplateKey(
                tenantId,
                request.TemplateDto.TemplateKey,
                request.TemplateDto.Version))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A template with the same TemplateKey and Version already exists for this tenant."],
                "Event template creation failed.");
        }

        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
            tenantId,
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
                    tenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            tenantId,
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
                    tenantId));
        }

        var definitionsResult = BuildDefinitionEntities(request.TemplateDto.Definitions, tenantId, createdAt, definitionIds);
        if (definitionsResult.Errors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                definitionsResult.Errors,
                "Event template creation failed.");
        }

        var dto = request.TemplateDto;
        var template = new EventTemplate
        {
            Id = templateId,
            CreatedAt = createdAt,
            TemplateKey = dto.TemplateKey,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            EventTypeId = dto.EventTypeId,
            Version = dto.Version,
            IsPublished = dto.IsPublished,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
            TenantId = tenantId,
            CreatedBy = _currentUserService.UserId,
            UpdatedBy = _currentUserService.UserId
        };

        template = await _eventTemplateRepository.CreateWithDefinitions(
            template, definitionsResult.Definitions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return BaseCommandResponse.Success(template.Id, "Event template created successfully.");
    }

    private (IReadOnlyCollection<TemplateDefinitionWithOptions> Definitions, List<string> Errors) BuildDefinitionEntities(
        IReadOnlyList<CreateEventTemplateDefinitionDto> definitionDtos, Guid tenantId, DateTime createdAt,
        (Guid Id, Guid[] OptionIds)[] definitionIds)
    {
        var errors = new List<string>();
        var definitions = new List<TemplateDefinitionWithOptions>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < definitionDtos.Count; index++)
        {
            var defDto = definitionDtos[index];
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
                Id = definitionIds[index].Id,
                CreatedAt = createdAt,
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
                TenantId = tenantId,
                CreatedBy = _currentUserService.UserId,
                UpdatedBy = _currentUserService.UserId
            };

            var options = CreateOptionEntities(defDto.Options, definition.Id, createdAt, definitionIds[index].OptionIds);
            var defaultOption = options.SingleOrDefault(x => x.IsDefault);

            definitions.Add(new TemplateDefinitionWithOptions(definition, options, defaultOption?.Id));
        }

        return (definitions, errors);
    }

    private List<EventTemplateCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<CreateEventTemplateOptionDto> optionDtos,
        Guid definitionId, DateTime createdAt, Guid[] optionIds)
    {
        return optionDtos
            .Select((optionDto, index) => new EventTemplateCustomPropertyOption
            {
                Id = optionIds[index],
                CreatedAt = createdAt,
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
