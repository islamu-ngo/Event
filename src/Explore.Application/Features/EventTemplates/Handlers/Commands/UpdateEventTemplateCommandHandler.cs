using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.DTOs.EventTemplate.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTemplates.Handlers.Commands;

public class UpdateEventTemplateCommandHandler : IRequestHandler<UpdateEventTemplateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateEventTemplateCommandHandler(
        IEventTemplateRepository eventTemplateRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ICurrentUserService currentUserService,
        IMapper mapper,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _eventTemplateRepository = eventTemplateRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventTemplateCommand request, CancellationToken cancellationToken)
    {
        if (request.TemplateId == Guid.Empty || request.ExpectedConcurrencyStamp == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["TemplateId and ExpectedConcurrencyStamp are required."],
                "Event template update failed.");
        }

        var validator = new UpdateEventTemplateDtoValidator();
        var validationResult = await validator.ValidateAsync(request.TemplateDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event template update failed.");
        }

        var template = await _eventTemplateRepository.GetTrackedTemplateWithDefinitions(request.TemplateId, cancellationToken);
        if (template == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event template not found.");
        }

        request = request with { TenantId = template.TenantId };

        if (request.TenantId == Guid.Empty || request.TenantId != template.TenantId)
        {
            return BaseCommandResponse.NotFound<Guid>("Event template not found.");
        }

        if (template.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event template changed since it was loaded. Reload and try again.",
                "event_template",
                template.Id.ToString());
        }

        var candidate = new CreateEventTemplateDto
        {
            TemplateKey = template.TemplateKey,
            DisplayName = template.DisplayName,
            Description = template.Description,
            EventTypeId = template.EventTypeId,
            Version = template.Version,
            IsPublished = template.IsPublished,
            IsActive = template.IsActive,
            SortOrder = template.SortOrder,
            Definitions = request.TemplateDto.Definitions?.Items ?? []
        };
        ApplyPatch(candidate, request.TemplateDto);

        var candidateValidation = await new CreateEventTemplateDtoValidator().ValidateAsync(candidate, cancellationToken);
        if (!candidateValidation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                candidateValidation.Errors.Select(error => error.ErrorMessage),
                "Event template update failed.");
        }

        if (await _eventTemplateRepository.ExistsTemplateKey(
                template.TenantId,
                candidate.TemplateKey,
                candidate.Version,
                template.Id))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A template with the same TemplateKey and Version already exists for this tenant."],
                "Event template update failed.");
        }

        IReadOnlyCollection<TemplateDefinitionWithOptions>? definitions = null;
        if (request.TemplateDto.Definitions is not null)
        {
            var maxDefinitions = await _quotaResolver.GetIntAsync(
                CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
                template.TenantId,
                cancellationToken);
            if (candidate.Definitions.Count > maxDefinitions)
            {
                return BaseCommandResponse.Quota<Guid>(
                    "Event template update failed.",
                    new QuotaExceededDetails(
                        CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTemplate.Key,
                        maxDefinitions,
                        null,
                        candidate.Definitions.Count,
                        "event_template_definitions",
                        template.TenantId));
            }

            var maxOptions = await _quotaResolver.GetIntAsync(
                CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                template.TenantId,
                cancellationToken);
            var overOptionDefinition = candidate.Definitions
                .FirstOrDefault(definition => definition.Options.Count > maxOptions);
            if (overOptionDefinition is not null)
            {
                return BaseCommandResponse.Quota<Guid>(
                    "Event template update failed.",
                    new QuotaExceededDetails(
                        CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                        maxOptions,
                        null,
                        overOptionDefinition.Options.Count,
                        "event_template_definition_options",
                        template.TenantId));
            }

            var definitionsResult = BuildDefinitionEntities(candidate.Definitions, template.TenantId);
            if (definitionsResult.Errors.Count > 0)
            {
                return BaseCommandResponse.Validation<Guid>(
                    definitionsResult.Errors,
                    "Event template update failed.");
            }

            definitions = definitionsResult.Definitions;
        }

        var previousEventTypeId = template.EventTypeId;
        template.TemplateKey = candidate.TemplateKey;
        template.DisplayName = candidate.DisplayName;
        template.Description = candidate.Description;
        template.EventTypeId = candidate.EventTypeId;
        template.Version = candidate.Version;
        template.IsPublished = candidate.IsPublished;
        template.IsActive = candidate.IsActive;
        template.SortOrder = candidate.SortOrder;
        template.UpdatedBy = _currentUserService.UserId;
        template.UpdatedAt = DateTime.UtcNow;

        if (definitions is null)
        {
            await _unitOfWork.ExecuteInTransactionAsync(
                _ => _eventTemplateRepository.Update(template),
                cancellationToken);
        }
        else
        {
            await _unitOfWork.ExecuteInTransactionAsync(
                ct => _eventTemplateRepository.UpdateWithDefinitions(template, definitions, ct),
                cancellationToken);
        }

        await InvalidateCaches(template.TenantId, previousEventTypeId, template.EventTypeId, template.Id, cancellationToken);

        return BaseCommandResponse.Success(template.Id, "Event template updated successfully.");
    }

    private static void ApplyPatch(CreateEventTemplateDto candidate, UpdateEventTemplateDto patch)
    {
        var metadata = patch.Metadata;
        if (metadata is null)
        {
            return;
        }

        candidate.TemplateKey = metadata.TemplateKey ?? candidate.TemplateKey;
        candidate.DisplayName = metadata.DisplayName ?? candidate.DisplayName;
        if (metadata.Description.HasValue) candidate.Description = metadata.Description.Value;
        if (metadata.EventTypeId.HasValue) candidate.EventTypeId = metadata.EventTypeId.Value;
        candidate.Version = metadata.Version ?? candidate.Version;
        candidate.IsPublished = metadata.IsPublished ?? candidate.IsPublished;
        candidate.IsActive = metadata.IsActive ?? candidate.IsActive;
        candidate.SortOrder = metadata.SortOrder ?? candidate.SortOrder;
    }

    private (IReadOnlyCollection<TemplateDefinitionWithOptions> Definitions, List<string> Errors) BuildDefinitionEntities(
        IReadOnlyList<CreateEventTemplateDefinitionDto> definitionDtos,
        Guid tenantId)
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

            var definition = _mapper.Map<EventTemplateCustomPropertyDefinition>(defDto);
            definition.TenantId = tenantId;
            definition.Namespace = governance.NormalizedNamespace;
            definition.Key = governance.NormalizedKey;
            definition.CreatedBy = _currentUserService.UserId;
            definition.UpdatedBy = _currentUserService.UserId;

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

    private async Task InvalidateCaches(
        Guid tenantId,
        int? previousEventTypeId,
        int? currentEventTypeId,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(
            $"event-templates:list:{tenantId}:{(int?)null}:1:{PaginatedResult<EventTemplateListDto>.DefaultPageSize}",
            cancellationToken);
        if (previousEventTypeId.HasValue)
        {
            await _cache.RemoveAsync($"event-templates:list:{tenantId}:{previousEventTypeId}:1:{PaginatedResult<EventTemplateListDto>.DefaultPageSize}", cancellationToken);
        }
        if (currentEventTypeId.HasValue && currentEventTypeId != previousEventTypeId)
        {
            await _cache.RemoveAsync($"event-templates:list:{tenantId}:{currentEventTypeId}:1:{PaginatedResult<EventTemplateListDto>.DefaultPageSize}", cancellationToken);
        }
        await _cache.RemoveAsync($"event-templates:detail:{templateId}", cancellationToken);
    }
}
