using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionCustomProperty.Validators;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public class CreateEventSessionCustomPropertyDefinitionCommandHandler : ICommandHandler<CreateEventSessionCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventSessionRepository _sessionRepository;

    public CreateEventSessionCustomPropertyDefinitionCommandHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        HybridCache cache,
        IUnitOfWork unitOfWork,
        IEventSessionRepository sessionRepository)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _sessionRepository = sessionRepository;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventSessionCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventSessionCustomPropertyDefinitionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.DefinitionDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session custom property definition creation failed.");
        }

        var session = await _sessionRepository.GetById(request.DefinitionDto.EventSessionId);
        if (session is null || session.TenantId != _tenantContext.TenantId)
        {
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
        }

        var governance = _customPropertyGovernancePolicy.EvaluateDefinition(request.DefinitionDto.Namespace, request.DefinitionDto.Key);
        if (!governance.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                governance.Errors,
                "Event session custom property definition creation failed.");
        }

        if (await _sessionCustomPropertyRepository.ExistsDefinitionKey(
                request.DefinitionDto.EventSessionId,
                governance.NormalizedNamespace,
                governance.NormalizedKey))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A custom property definition with the same Namespace + Key already exists for this session."],
                "Event session custom property definition creation failed.");
        }

        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key,
            _tenantContext.TenantId,
            cancellationToken);
        var currentDefinitionCount = await _sessionCustomPropertyRepository.CountDefinitionsForSession(
            request.DefinitionDto.EventSessionId,
            cancellationToken);
        if (currentDefinitionCount >= maxDefinitions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event session custom property definition creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key,
                    maxDefinitions,
                    currentDefinitionCount,
                    currentDefinitionCount + 1,
                    "event_session_custom_property_definitions",
                    _tenantContext.TenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            _tenantContext.TenantId,
            cancellationToken);
        if (request.DefinitionDto.Options.Count > maxOptions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Event session custom property definition creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    null,
                    request.DefinitionDto.Options.Count,
                    "event_session_custom_property_options",
                    _tenantContext.TenantId));
        }

        var dto = request.DefinitionDto;
        var definition = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = dto.EventSessionId,
            Namespace = governance.NormalizedNamespace,
            Key = governance.NormalizedKey,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            PropertyType = dto.PropertyType,
            IsRequired = dto.IsRequired,
            IsMulti = dto.IsMulti,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder,
            ExposureLevel = dto.ExposureLevel,
            IsSearchable = dto.IsSearchable,
            IsFilterable = dto.IsFilterable,
            IsExportable = dto.IsExportable,
            IsModerationRelevant = dto.IsModerationRelevant,
            IsAnalyticsRelevant = dto.IsAnalyticsRelevant,
            IsSystemOwned = dto.IsSystemOwned,
            DefaultTextValue = dto.DefaultTextValue,
            DefaultNumberValue = dto.DefaultNumberValue,
            DefaultBooleanValue = dto.DefaultBooleanValue,
            DefaultDateTimeValue = dto.DefaultDateTimeValue,
            MinLength = dto.MinLength,
            MaxLength = dto.MaxLength,
            RegexPattern = dto.RegexPattern,
            MinNumber = dto.MinNumber,
            MaxNumber = dto.MaxNumber,
            MinDateTime = dto.MinDateTime,
            MaxDateTime = dto.MaxDateTime,
            AllowedUrlSchemes = dto.AllowedUrlSchemes,
            TenantId = _tenantContext.TenantId,
            InstantiatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUserService.UserId,
            UpdatedBy = _currentUserService.UserId
        };

        var options = CreateOptionEntities(request.DefinitionDto.Options, definition.Id);
        var defaultOption = options.SingleOrDefault(x => x.IsDefault);

        definition = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _sessionCustomPropertyRepository.CreateWithOptions(definition, options, defaultOption?.Id, ct),
            cancellationToken);

        await _cache.RemoveByTagAsync(
            SessionCustomPropertyCache.ListsBySession(definition.TenantId, definition.EventSessionId),
            CancellationToken.None);

        return BaseCommandResponse.Success(definition.Id, "Event session custom property definition created successfully.");
    }

    private List<EventSessionCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<DTOs.EventSessionCustomProperty.CreateEventSessionCustomPropertyOptionDto> optionDtos,
        Guid definitionId)
    {
        return optionDtos
            .Select(optionDto => new EventSessionCustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                EventSessionCustomPropertyDefinitionId = definitionId,
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

}
