using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventCustomProperty.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Commands;

public class UpdateEventCustomPropertyDefinitionCommandHandler : IRequestHandler<UpdateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IEventCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateEventCustomPropertyDefinitionCommandHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IEventCustomPropertyProjectionUpdater projectionUpdater,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ICurrentUserService currentUserService,
        IMapper mapper,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        if (request.DefinitionId == Guid.Empty || request.ExpectedConcurrencyStamp == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["DefinitionId and ExpectedConcurrencyStamp are required."],
                "Event custom property definition update failed.");
        }

        var validator = new UpdateEventCustomPropertyDefinitionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.DefinitionDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event custom property definition update failed.");
        }

        var definition = await _eventCustomPropertyRepository.GetTrackedDefinitionWithOptions(request.DefinitionId, cancellationToken);
        if (definition == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event custom property definition not found.");
        }

        request = request with { TenantId = definition.TenantId };

        if (request.TenantId == Guid.Empty || request.TenantId != definition.TenantId)
        {
            return BaseCommandResponse.NotFound<Guid>("Event custom property definition not found.");
        }

        if (definition.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event custom-property definition changed since it was loaded. Reload and try again.",
                "event_custom_property_definition",
                definition.Id.ToString());
        }

        var candidate = new CreateEventCustomPropertyDefinitionDto
        {
            EventId = definition.EventId,
            Namespace = definition.Namespace,
            Key = definition.Key,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            PropertyType = definition.PropertyType,
            IsRequired = definition.IsRequired,
            IsMulti = definition.IsMulti,
            IsActive = definition.IsActive,
            SortOrder = definition.SortOrder,
            ExposureLevel = definition.ExposureLevel,
            IsSearchable = definition.IsSearchable,
            IsFilterable = definition.IsFilterable,
            IsExportable = definition.IsExportable,
            IsModerationRelevant = definition.IsModerationRelevant,
            IsAnalyticsRelevant = definition.IsAnalyticsRelevant,
            IsSystemOwned = definition.IsSystemOwned,
            DefaultTextValue = definition.DefaultTextValue,
            DefaultNumberValue = definition.DefaultNumberValue,
            DefaultBooleanValue = definition.DefaultBooleanValue,
            DefaultDateTimeValue = definition.DefaultDateTimeValue,
            DefaultOptionId = definition.DefaultOptionId,
            MinLength = definition.MinLength,
            MaxLength = definition.MaxLength,
            RegexPattern = definition.RegexPattern,
            MinNumber = definition.MinNumber,
            MaxNumber = definition.MaxNumber,
            MinDateTime = definition.MinDateTime,
            MaxDateTime = definition.MaxDateTime,
            AllowedUrlSchemes = definition.AllowedUrlSchemes,
            Options = definition.Options.Select(ToCreateOptionDto).ToList()
        };
        candidate = ApplyPatch(candidate, request.DefinitionDto);

        var candidateValidation = await new CreateEventCustomPropertyDefinitionDtoValidator().ValidateAsync(candidate, cancellationToken);
        if (!candidateValidation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                candidateValidation.Errors.Select(error => error.ErrorMessage),
                "Event custom property definition update failed.");
        }

        var governance = _customPropertyGovernancePolicy.EvaluateDefinition(candidate.Namespace, candidate.Key);
        if (!governance.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                governance.Errors,
                "Event custom property definition update failed.");
        }

        if (await _eventCustomPropertyRepository.ExistsDefinitionKey(
                definition.EventId,
                governance.NormalizedNamespace,
                governance.NormalizedKey,
                definition.Id))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A custom property definition with the same Namespace + Key already exists for this event."],
                "Event custom property definition update failed.");
        }

        if (request.DefinitionDto.Options is not null)
        {
            var maxOptions = await _quotaResolver.GetIntAsync(
                CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                definition.TenantId,
                cancellationToken);
            if (candidate.Options.Count > maxOptions)
            {
                return BaseCommandResponse.Quota<Guid>(
                    "Event custom property definition update failed.",
                    new QuotaExceededDetails(
                        CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                        maxOptions,
                        null,
                        candidate.Options.Count,
                        "event_custom_property_options",
                        definition.TenantId));
            }
        }

        _mapper.Map(candidate, definition);
        definition.Namespace = governance.NormalizedNamespace;
        definition.Key = governance.NormalizedKey;
        definition.UpdatedBy = _currentUserService.UserId;
        definition.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                if (request.DefinitionDto.Options is null)
                {
                    await _eventCustomPropertyRepository.Update(definition);
                }
                else
                {
                    var options = CreateOptionEntities(candidate.Options, definition.Id);
                    var defaultOption = options.SingleOrDefault(x => x.IsDefault);
                    await _eventCustomPropertyRepository.UpdateWithOptions(definition, options, defaultOption?.Id, ct);
                }

                await _projectionUpdater.UpdateForDefinitionAsync(definition.Id, ct);
            },
            cancellationToken);

        await InvalidateCaches(definition.EventId, definition.Id, cancellationToken);

        return BaseCommandResponse.Success(definition.Id, "Event custom property definition updated successfully.");
    }

    private List<EventCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<CreateEventCustomPropertyOptionDto> optionDtos,
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

    private static CreateEventCustomPropertyOptionDto ToCreateOptionDto(EventCustomPropertyOption option) => new()
    {
        Namespace = option.Namespace,
        Key = option.Key,
        DisplayName = option.DisplayName,
        Description = option.Description,
        Value = option.Value,
        IsDefault = option.IsDefault,
        IsActive = option.IsActive,
        SortOrder = option.SortOrder
    };

    private static CreateEventCustomPropertyDefinitionDto ApplyPatch(
        CreateEventCustomPropertyDefinitionDto candidate,
        UpdateEventCustomPropertyDefinitionDto patch)
    {
        var metadata = patch.Metadata;
        if (metadata is not null)
        {
            candidate.Namespace = metadata.Namespace ?? candidate.Namespace;
            candidate.Key = metadata.Key ?? candidate.Key;
            candidate.DisplayName = metadata.DisplayName ?? candidate.DisplayName;
            if (metadata.Description.HasValue) candidate.Description = metadata.Description.Value;
            candidate.IsActive = metadata.IsActive ?? candidate.IsActive;
            candidate.SortOrder = metadata.SortOrder ?? candidate.SortOrder;
            candidate.ExposureLevel = metadata.ExposureLevel ?? candidate.ExposureLevel;
            candidate.IsSearchable = metadata.IsSearchable ?? candidate.IsSearchable;
            candidate.IsFilterable = metadata.IsFilterable ?? candidate.IsFilterable;
            candidate.IsExportable = metadata.IsExportable ?? candidate.IsExportable;
            candidate.IsModerationRelevant = metadata.IsModerationRelevant ?? candidate.IsModerationRelevant;
            candidate.IsAnalyticsRelevant = metadata.IsAnalyticsRelevant ?? candidate.IsAnalyticsRelevant;
            candidate.IsSystemOwned = metadata.IsSystemOwned ?? candidate.IsSystemOwned;
        }

        var validation = patch.Validation;
        if (validation is not null)
        {
            candidate.PropertyType = validation.PropertyType ?? candidate.PropertyType;
            candidate.IsRequired = validation.IsRequired ?? candidate.IsRequired;
            candidate.IsMulti = validation.IsMulti ?? candidate.IsMulti;
            if (validation.DefaultTextValue.HasValue) candidate.DefaultTextValue = validation.DefaultTextValue.Value;
            if (validation.DefaultNumberValue.HasValue) candidate.DefaultNumberValue = validation.DefaultNumberValue.Value;
            if (validation.DefaultBooleanValue.HasValue) candidate.DefaultBooleanValue = validation.DefaultBooleanValue.Value;
            if (validation.DefaultDateTimeValue.HasValue) candidate.DefaultDateTimeValue = validation.DefaultDateTimeValue.Value;
            if (validation.MinLength.HasValue) candidate.MinLength = validation.MinLength.Value;
            if (validation.MaxLength.HasValue) candidate.MaxLength = validation.MaxLength.Value;
            if (validation.RegexPattern.HasValue) candidate.RegexPattern = validation.RegexPattern.Value;
            if (validation.MinNumber.HasValue) candidate.MinNumber = validation.MinNumber.Value;
            if (validation.MaxNumber.HasValue) candidate.MaxNumber = validation.MaxNumber.Value;
            if (validation.MinDateTime.HasValue) candidate.MinDateTime = validation.MinDateTime.Value;
            if (validation.MaxDateTime.HasValue) candidate.MaxDateTime = validation.MaxDateTime.Value;
            if (validation.AllowedUrlSchemes.HasValue) candidate.AllowedUrlSchemes = validation.AllowedUrlSchemes.Value;
        }

        return patch.Options is null
            ? candidate
            : candidate with { Options = patch.Options.Items! };
    }

    private async Task InvalidateCaches(Guid eventId, Guid definitionId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(
            $"event-custom-properties:list:{eventId}:1:{PaginatedResult<EventCustomPropertyDefinitionListDto>.DefaultPageSize}",
            cancellationToken);
        await _cache.RemoveAsync($"event-custom-properties:detail:{definitionId}", cancellationToken);
    }
}
