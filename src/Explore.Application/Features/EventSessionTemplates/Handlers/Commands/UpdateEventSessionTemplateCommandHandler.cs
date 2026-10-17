using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventSessionTemplate.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Commands;

public class UpdateEventSessionTemplateCommandHandler : ICommandHandler<UpdateEventSessionTemplateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly HybridCache _cache;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<UpdateEventSessionTemplateCommand> _authorization;
    private readonly ILogger<UpdateEventSessionTemplateCommandHandler> _logger;

    public UpdateEventSessionTemplateCommandHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ICurrentUserService currentUserService,
        HybridCache cache,
        ISerializableAttemptOwner owner,
        ITenantContext tenantContext,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<UpdateEventSessionTemplateCommand> authorization,
        ILogger<UpdateEventSessionTemplateCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _currentUserService = currentUserService;
        _cache = cache;
        _owner = owner;
        _preparation = preparation;
        _tenantContext = tenantContext;
        _adminContext = adminContext;
        _tenantAuthority = tenantAuthority;
        _instanceAuthority = instanceAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateEventSessionTemplateCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validationResult = await new UpdateEventSessionTemplateDtoValidator()
            .ValidateAsync(request.SessionTemplateDto, cancellationToken);
        if (!validationResult.IsValid)
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage), "Event session template update failed.");
        DateTime createdAt = DateTime.UtcNow;
        var definitionIds = (request.SessionTemplateDto.Definitions?.Items ?? []).Select(definition =>
            (Id: Guid.CreateVersion7(), OptionIds: definition.Options.Select(_ => Guid.CreateVersion7()).ToArray())).ToArray();
        EventSessionTemplate? updatedTemplate = null;
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        var response = await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            updatedTemplate = null;
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return BaseCommandResponse.Authorization<Guid>();
            return await UpdateAuthorizedAsync(request, tenantId, createdAt, definitionIds, template => updatedTemplate = template, token);
        }, cancellationToken);
        if (response.IsSuccess && updatedTemplate is not null)
            await InvalidateCaches(updatedTemplate.EventTemplateId, updatedTemplate.Id, cancellationToken);
        return response;
    }

    private async Task<BaseCommandResponse<Guid>> UpdateAuthorizedAsync(
        UpdateEventSessionTemplateCommand request, Guid executionTenantId, DateTime createdAt,
        (Guid Id, Guid[] OptionIds)[] definitionIds,
        Action<EventSessionTemplate> recordChange, CancellationToken cancellationToken)
    {
        if (request.SessionTemplateId == Guid.Empty || request.ExpectedConcurrencyStamp == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["SessionTemplateId and ExpectedConcurrencyStamp are required."],
                "Event session template update failed.");
        }

        var sessionTemplate = await _sessionTemplateRepository.GetTrackedSessionTemplateWithDefinitions(request.SessionTemplateId, cancellationToken);
        if (sessionTemplate == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session template not found.");
        }

        if (sessionTemplate.TenantId != executionTenantId)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session template not found.");
        }

        if (sessionTemplate.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event session template changed since it was loaded. Reload and try again.",
                "event_session_template",
                sessionTemplate.Id.ToString());
        }

        var candidate = new CreateEventSessionTemplateDto
        {
            EventTemplateId = sessionTemplate.EventTemplateId,
            SessionTemplateKey = sessionTemplate.SessionTemplateKey,
            DisplayName = sessionTemplate.DisplayName,
            Description = sessionTemplate.Description,
            Version = sessionTemplate.Version,
            IsPublished = sessionTemplate.IsPublished,
            IsActive = sessionTemplate.IsActive,
            SortOrder = sessionTemplate.SortOrder,
            Definitions = request.SessionTemplateDto.Definitions?.Items ?? []
        };
        ApplyPatch(candidate, request.SessionTemplateDto);

        var candidateValidation = await new CreateEventSessionTemplateDtoValidator().ValidateAsync(candidate, cancellationToken);
        if (!candidateValidation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                candidateValidation.Errors.Select(error => error.ErrorMessage),
                "Event session template update failed.");
        }

        if (await _sessionTemplateRepository.ExistsSessionTemplateKey(
                sessionTemplate.EventTemplateId,
                candidate.SessionTemplateKey,
                candidate.Version,
                sessionTemplate.Id))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A session template with the same SessionTemplateKey and Version already exists for this event template."],
                "Event session template update failed.");
        }

        IReadOnlyCollection<SessionTemplateDefinitionWithOptions>? definitions = null;
        if (request.SessionTemplateDto.Definitions is not null)
        {
            var maxDefinitions = await _quotaResolver.GetIntAsync(
                CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
                sessionTemplate.TenantId,
                cancellationToken);
            if (candidate.Definitions.Count > maxDefinitions)
            {
                return BaseCommandResponse.Quota<Guid>(
                    "Event session template update failed.",
                    new QuotaExceededDetails(
                        CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
                        maxDefinitions,
                        null,
                        candidate.Definitions.Count,
                        "event_session_template_definitions",
                        sessionTemplate.TenantId));
            }

            var maxOptions = await _quotaResolver.GetIntAsync(
                CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                sessionTemplate.TenantId,
                cancellationToken);
            var overOptionDefinition = candidate.Definitions
                .FirstOrDefault(definition => definition.Options.Count > maxOptions);
            if (overOptionDefinition is not null)
            {
                return BaseCommandResponse.Quota<Guid>(
                    "Event session template update failed.",
                    new QuotaExceededDetails(
                        CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                        maxOptions,
                        null,
                        overOptionDefinition.Options.Count,
                        "event_session_template_definition_options",
                        sessionTemplate.TenantId));
            }

            var definitionsResult = BuildDefinitionEntities(candidate.Definitions, sessionTemplate.TenantId, createdAt, definitionIds);
            if (definitionsResult.Errors.Count > 0)
            {
                return BaseCommandResponse.Validation<Guid>(
                    definitionsResult.Errors,
                    "Event session template update failed.");
            }

            definitions = definitionsResult.Definitions;
        }

        sessionTemplate.SessionTemplateKey = candidate.SessionTemplateKey;
        sessionTemplate.DisplayName = candidate.DisplayName;
        sessionTemplate.Description = candidate.Description;
        sessionTemplate.Version = candidate.Version;
        sessionTemplate.IsPublished = candidate.IsPublished;
        sessionTemplate.IsActive = candidate.IsActive;
        sessionTemplate.SortOrder = candidate.SortOrder;
        sessionTemplate.UpdatedBy = _currentUserService.UserId;
        sessionTemplate.UpdatedAt = DateTime.UtcNow;

        if (definitions is null)
        {
            await _sessionTemplateRepository.Update(sessionTemplate);
        }
        else
        {
            await _sessionTemplateRepository.UpdateWithDefinitions(sessionTemplate, definitions, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        recordChange(sessionTemplate);

        return BaseCommandResponse.Success(sessionTemplate.Id, "Event session template updated successfully.");
    }

    private static void ApplyPatch(CreateEventSessionTemplateDto candidate, UpdateEventSessionTemplateDto patch)
    {
        var metadata = patch.Metadata;
        if (metadata is null)
        {
            return;
        }

        candidate.SessionTemplateKey = metadata.SessionTemplateKey ?? candidate.SessionTemplateKey;
        candidate.DisplayName = metadata.DisplayName ?? candidate.DisplayName;
        if (metadata.Description.HasValue) candidate.Description = metadata.Description.Value;
        candidate.Version = metadata.Version ?? candidate.Version;
        candidate.IsPublished = metadata.IsPublished ?? candidate.IsPublished;
        candidate.IsActive = metadata.IsActive ?? candidate.IsActive;
        candidate.SortOrder = metadata.SortOrder ?? candidate.SortOrder;
    }

    private (IReadOnlyCollection<SessionTemplateDefinitionWithOptions> Definitions, List<string> Errors) BuildDefinitionEntities(
        IReadOnlyList<CreateEventSessionTemplateDefinitionDto> definitionDtos,
        Guid tenantId, DateTime createdAt, (Guid Id, Guid[] OptionIds)[] definitionIds)
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

    private async Task InvalidateCaches(Guid eventTemplateId, Guid sessionTemplateId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(
            $"session-templates:list:{eventTemplateId}:1:{PaginatedResult<EventSessionTemplateListDto>.DefaultPageSize}",
            cancellationToken);
        await _cache.RemoveAsync($"session-templates:detail:{sessionTemplateId}", cancellationToken);
    }
}
