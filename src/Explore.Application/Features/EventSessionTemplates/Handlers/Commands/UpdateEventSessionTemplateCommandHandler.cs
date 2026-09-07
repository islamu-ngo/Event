using AutoMapper;
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
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Commands;

public class UpdateEventSessionTemplateCommandHandler : IRequestHandler<UpdateEventSessionTemplateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateEventSessionTemplateCommandHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ICurrentUserService currentUserService,
        IMapper mapper,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventSessionTemplateCommand request, CancellationToken cancellationToken)
    {
        if (request.SessionTemplateId == Guid.Empty || request.ExpectedConcurrencyStamp == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["SessionTemplateId and ExpectedConcurrencyStamp are required."],
                "Event session template update failed.");
        }

        var validator = new UpdateEventSessionTemplateDtoValidator();
        var validationResult = await validator.ValidateAsync(request.SessionTemplateDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session template update failed.");
        }

        var sessionTemplate = await _sessionTemplateRepository.GetTrackedSessionTemplateWithDefinitions(request.SessionTemplateId, cancellationToken);
        if (sessionTemplate == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session template not found.");
        }

        request = request with { TenantId = sessionTemplate.TenantId };

        if (request.TenantId == Guid.Empty || request.TenantId != sessionTemplate.TenantId)
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

            var definitionsResult = BuildDefinitionEntities(candidate.Definitions, sessionTemplate.TenantId);
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
            await _unitOfWork.ExecuteInTransactionAsync(
                _ => _sessionTemplateRepository.Update(sessionTemplate),
                cancellationToken);
        }
        else
        {
            await _unitOfWork.ExecuteInTransactionAsync(
                ct => _sessionTemplateRepository.UpdateWithDefinitions(sessionTemplate, definitions, ct),
                cancellationToken);
        }

        await InvalidateCaches(sessionTemplate.EventTemplateId, sessionTemplate.Id, cancellationToken);

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
        Guid tenantId)
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
            definition.TenantId = tenantId;
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

    private async Task InvalidateCaches(Guid eventTemplateId, Guid sessionTemplateId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(
            $"session-templates:list:{eventTemplateId}:1:{PaginatedResult<EventSessionTemplateListDto>.DefaultPageSize}",
            cancellationToken);
        await _cache.RemoveAsync($"session-templates:detail:{sessionTemplateId}", cancellationToken);
    }
}
