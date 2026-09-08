using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.CustomPropertyDefinition.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;

public class UpdateCustomPropertyDefinitionCommandHandler : IRequestHandler<UpdateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCustomPropertyDefinitionCommandHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ICurrentUserService currentUserService,
        IMapper mapper,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        if (request.DefinitionId == Guid.Empty || request.ExpectedConcurrencyStamp == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["DefinitionId and ExpectedConcurrencyStamp are required."],
                "Custom-property definition update failed.");
        }

        var validator = new UpdateCustomPropertyDefinitionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.DefinitionDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Custom-property definition update failed.");
        }

        var definition = await _customPropertyDefinitionRepository.GetTrackedDefinitionWithOptions(request.DefinitionId, cancellationToken);
        if (definition == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Custom-property definition not found.");
        }

        request = request with { TenantId = definition.TenantId };

        if (request.TenantId == Guid.Empty || request.TenantId != definition.TenantId)
        {
            return BaseCommandResponse.NotFound<Guid>("Custom-property definition not found.");
        }

        if (definition.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The custom-property definition changed since it was loaded. Reload and try again.",
                "custom_property_definition",
                definition.Id.ToString());
        }

        var previousEntityTypeName = definition.EntityTypeName;

        var candidate = new CreateCustomPropertyDefinitionDto
        {
            EntityTypeName = definition.EntityTypeName,
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
            MinLength = definition.MinLength,
            MaxLength = definition.MaxLength,
            RegexPattern = definition.RegexPattern,
            MinNumber = definition.MinNumber,
            MaxNumber = definition.MaxNumber,
            MinDateTime = definition.MinDateTime,
            MaxDateTime = definition.MaxDateTime,
            AllowedUrlSchemes = definition.AllowedUrlSchemes,
            Options = definition.Options.Select(option => new CreateCustomPropertyOptionDto { Namespace = option.Namespace, Key = option.Key, DisplayName = option.DisplayName, Description = option.Description, Value = option.Value, IsDefault = option.IsDefault, IsActive = option.IsActive, SortOrder = option.SortOrder }).ToList()
        };
        candidate = ApplyPatch(candidate, request.DefinitionDto);
        var candidateValidation = await new CreateCustomPropertyDefinitionDtoValidator().ValidateAsync(candidate, cancellationToken);
        if (!candidateValidation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                candidateValidation.Errors.Select(error => error.ErrorMessage),
                "Custom-property definition update failed.");
        }

        var governance = _customPropertyGovernancePolicy.EvaluateDefinition(candidate.Namespace, candidate.Key);
        if (!governance.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                governance.Errors,
                "Custom-property definition update failed.");
        }

        if (await _customPropertyDefinitionRepository.ExistsScopedMachineKey(
                definition.TenantId,
                candidate.EntityTypeName,
                governance.NormalizedNamespace,
                governance.NormalizedKey,
                definition.Id))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A custom-property definition with the same Namespace + Key already exists in this scope."],
                "Custom-property definition update failed.");
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
                    "Custom-property definition update failed.",
                    new QuotaExceededDetails(
                        CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                        maxOptions,
                        null,
                        candidate.Options.Count,
                        "custom_property_definition_options",
                        definition.TenantId));
            }
        }

        _mapper.Map(candidate, definition);
        definition.Namespace = governance.NormalizedNamespace;
        definition.Key = governance.NormalizedKey;
        definition.UpdatedBy = _currentUserService.UserId;
        definition.UpdatedAt = DateTime.UtcNow;

        if (request.DefinitionDto.Options is null)
        {
            await _unitOfWork.ExecuteInTransactionAsync(
                _ => _customPropertyDefinitionRepository.Update(definition),
                cancellationToken);
        }
        else
        {
            var options = CreateOptionEntities(candidate.Options, definition.Id);
            var defaultOption = options.SingleOrDefault(x => x.IsDefault);
            await _unitOfWork.ExecuteInTransactionAsync(
                ct => _customPropertyDefinitionRepository.UpdateWithOptions(definition, options, defaultOption?.Id, ct),
                cancellationToken);
        }

        await InvalidateCaches(previousEntityTypeName, definition.EntityTypeName, definition.Id, cancellationToken);

        return BaseCommandResponse.Success(definition.Id, "Custom-property definition updated successfully.");
    }

    private List<CustomPropertyOption> CreateOptionEntities(IReadOnlyCollection<CreateCustomPropertyOptionDto> optionDtos, Guid definitionId)
    {
        return optionDtos
            .Select(optionDto => new CustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                CustomPropertyDefinitionId = definitionId,
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

    private static CreateCustomPropertyDefinitionDto ApplyPatch(CreateCustomPropertyDefinitionDto candidate, UpdateCustomPropertyDefinitionDto patch)
    {
        if (patch.Relations?.EntityTypeName is { } entityTypeName)
        {
            candidate.EntityTypeName = entityTypeName;
        }

        var m = patch.Metadata;
        if (m is not null)
        {
            candidate = candidate with
            {
                Namespace = m.Namespace ?? candidate.Namespace,
                Key = m.Key ?? candidate.Key,
                DisplayName = m.DisplayName ?? candidate.DisplayName
            };
            if (m.Description.HasValue) candidate.Description = m.Description.Value;
            candidate.IsActive = m.IsActive ?? candidate.IsActive; candidate.SortOrder = m.SortOrder ?? candidate.SortOrder; candidate.ExposureLevel = m.ExposureLevel ?? candidate.ExposureLevel;
            candidate.IsSearchable = m.IsSearchable ?? candidate.IsSearchable; candidate.IsFilterable = m.IsFilterable ?? candidate.IsFilterable; candidate.IsExportable = m.IsExportable ?? candidate.IsExportable; candidate.IsModerationRelevant = m.IsModerationRelevant ?? candidate.IsModerationRelevant; candidate.IsAnalyticsRelevant = m.IsAnalyticsRelevant ?? candidate.IsAnalyticsRelevant; candidate.IsSystemOwned = m.IsSystemOwned ?? candidate.IsSystemOwned;
        }
        var v = patch.Validation;
        if (v is not null)
        {
            candidate.PropertyType = v.PropertyType ?? candidate.PropertyType; candidate.IsRequired = v.IsRequired ?? candidate.IsRequired; candidate.IsMulti = v.IsMulti ?? candidate.IsMulti;
            if (v.DefaultTextValue.HasValue) candidate.DefaultTextValue = v.DefaultTextValue.Value; if (v.DefaultNumberValue.HasValue) candidate.DefaultNumberValue = v.DefaultNumberValue.Value; if (v.DefaultBooleanValue.HasValue) candidate.DefaultBooleanValue = v.DefaultBooleanValue.Value; if (v.DefaultDateTimeValue.HasValue) candidate.DefaultDateTimeValue = v.DefaultDateTimeValue.Value; if (v.MinLength.HasValue) candidate.MinLength = v.MinLength.Value; if (v.MaxLength.HasValue) candidate.MaxLength = v.MaxLength.Value; if (v.RegexPattern.HasValue) candidate.RegexPattern = v.RegexPattern.Value; if (v.MinNumber.HasValue) candidate.MinNumber = v.MinNumber.Value; if (v.MaxNumber.HasValue) candidate.MaxNumber = v.MaxNumber.Value; if (v.MinDateTime.HasValue) candidate.MinDateTime = v.MinDateTime.Value; if (v.MaxDateTime.HasValue) candidate.MaxDateTime = v.MaxDateTime.Value; if (v.AllowedUrlSchemes.HasValue) candidate.AllowedUrlSchemes = v.AllowedUrlSchemes.Value;
        }
        return patch.Options is null
            ? candidate
            : candidate with { Options = patch.Options.Items! };
    }

    private async Task InvalidateCaches(
        EntityTypeName previousEntityTypeName,
        EntityTypeName currentEntityTypeName,
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync($"custom-property-definitions:list:{previousEntityTypeName}:1:{PaginatedResult<CustomPropertyDefinitionListDto>.DefaultPageSize}", cancellationToken);
        if (currentEntityTypeName != previousEntityTypeName)
        {
            await _cache.RemoveAsync($"custom-property-definitions:list:{currentEntityTypeName}:1:{PaginatedResult<CustomPropertyDefinitionListDto>.DefaultPageSize}", cancellationToken);
        }

        await _cache.RemoveAsync($"custom-property-definitions:detail:{definitionId}", cancellationToken);
    }
}
