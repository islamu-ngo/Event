using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
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
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Commands;

public class CreateEventSessionTemplateCommandHandler : ICommandHandler<CreateEventSessionTemplateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly HybridCache _cache;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<CreateEventSessionTemplateCommand> _authorization;
    private readonly ILogger<CreateEventSessionTemplateCommandHandler> _logger;

    public CreateEventSessionTemplateCommandHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        HybridCache cache,
        ISerializableAttemptOwner owner,
        IEventTemplateRepository eventTemplateRepository,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<CreateEventSessionTemplateCommand> authorization,
        ILogger<CreateEventSessionTemplateCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _cache = cache;
        _owner = owner;
        _preparation = preparation;
        _eventTemplateRepository = eventTemplateRepository;
        _adminContext = adminContext;
        _tenantAuthority = tenantAuthority;
        _instanceAuthority = instanceAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventSessionTemplateCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        request = request with { SessionTemplateDto = request.SessionTemplateDto with { } };
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validationResult = await new CreateEventSessionTemplateDtoValidator()
            .ValidateAsync(request.SessionTemplateDto, cancellationToken);
        if (!validationResult.IsValid)
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage), "Event session template creation failed.");
        Guid templateId = Guid.CreateVersion7();
        DateTime createdAt = DateTime.UtcNow;
        var definitionIds = request.SessionTemplateDto.Definitions.Select(definition =>
            (Id: Guid.CreateVersion7(), OptionIds: definition.Options.Select(_ => Guid.CreateVersion7()).ToArray())).ToArray();
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        var response = await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return BaseCommandResponse.Authorization<Guid>();
            var parent = await _eventTemplateRepository.GetTrackedTemplateWithDefinitions(
                request.SessionTemplateDto.EventTemplateId, token);
            if (parent is null || parent.IsDeleted || parent.TenantId != tenantId)
                return BaseCommandResponse.NotFound<Guid>("Event template not found.");
            return await CreateAuthorizedAsync(request, tenantId, templateId, createdAt, definitionIds, token);
        }, cancellationToken);
        if (response.IsSuccess)
            await _cache.RemoveAsync(
                GetListCacheKey(request.SessionTemplateDto.EventTemplateId, 1, PaginatedResult<object>.DefaultPageSize),
                cancellationToken);
        return response;
    }

    private async Task<BaseCommandResponse<Guid>> CreateAuthorizedAsync(
        CreateEventSessionTemplateCommand request, Guid tenantId, Guid templateId, DateTime createdAt,
        (Guid Id, Guid[] OptionIds)[] definitionIds, CancellationToken cancellationToken)
    {
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
            tenantId,
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
                    tenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            tenantId,
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
                    tenantId));
        }

        var definitionsResult = BuildDefinitionEntities(request.SessionTemplateDto.Definitions, tenantId, createdAt, definitionIds);
        if (definitionsResult.Errors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                definitionsResult.Errors,
                "Event session template creation failed.");
        }

        var dto = request.SessionTemplateDto;
        var sessionTemplate = new EventSessionTemplate
        {
            Id = templateId,
            CreatedAt = createdAt,
            EventTemplateId = dto.EventTemplateId,
            SessionTemplateKey = dto.SessionTemplateKey,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Version = dto.Version,
            IsPublished = dto.IsPublished,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
            TenantId = tenantId,
            CreatedBy = _currentUserService.UserId,
            UpdatedBy = _currentUserService.UserId
        };

        sessionTemplate = await _sessionTemplateRepository.CreateWithDefinitions(
            sessionTemplate, definitionsResult.Definitions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return BaseCommandResponse.Success(sessionTemplate.Id, "Event session template created successfully.");
    }

    private (IReadOnlyCollection<SessionTemplateDefinitionWithOptions> Definitions, List<string> Errors) BuildDefinitionEntities(
        IReadOnlyList<CreateEventSessionTemplateDefinitionDto> definitionDtos, Guid tenantId, DateTime createdAt,
        (Guid Id, Guid[] OptionIds)[] definitionIds)
    {
        var errors = new List<string>();
        var definitions = new List<SessionTemplateDefinitionWithOptions>();
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
                errors.Add($"Duplicate definition key '{compositeKey}' within the same session template.");
                continue;
            }

            var definition = new EventSessionTemplateCustomPropertyDefinition
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

            definitions.Add(new SessionTemplateDefinitionWithOptions(definition, options, defaultOption?.Id));
        }

        return (definitions, errors);
    }

    private List<EventSessionTemplateCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<CreateEventSessionTemplateOptionDto> optionDtos,
        Guid definitionId, DateTime createdAt, Guid[] optionIds)
    {
        return optionDtos
            .Select((optionDto, index) => new EventSessionTemplateCustomPropertyOption
            {
                Id = optionIds[index],
                CreatedAt = createdAt,
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
