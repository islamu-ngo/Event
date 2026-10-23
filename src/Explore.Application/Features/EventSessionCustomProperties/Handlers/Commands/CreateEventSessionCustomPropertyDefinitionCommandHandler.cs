using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
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
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public class CreateEventSessionCustomPropertyDefinitionCommandHandler : ICommandHandler<CreateEventSessionCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly HybridCache _cache;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<CreateEventSessionCustomPropertyDefinitionCommand> _authorization;
    private readonly ILogger<CreateEventSessionCustomPropertyDefinitionCommandHandler> _logger;

    public CreateEventSessionCustomPropertyDefinitionCommandHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        HybridCache cache,
        ISerializableAttemptOwner owner,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<CreateEventSessionCustomPropertyDefinitionCommand> authorization,
        ILogger<CreateEventSessionCustomPropertyDefinitionCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _cache = cache;
        _owner = owner;
        _preparation = preparation;
        _adminContext = adminContext;
        _tenantAuthority = tenantAuthority;
        _instanceAuthority = instanceAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventSessionCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        request = request with { DefinitionDto = request.DefinitionDto with { } };
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validator = new CreateEventSessionCustomPropertyDefinitionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.DefinitionDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session custom property definition creation failed.");
        }

        Guid definitionId = Guid.CreateVersion7();
        Guid[] optionIds = request.DefinitionDto.Options.Select(_ => Guid.CreateVersion7()).ToArray();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        var response = await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return BaseCommandResponse.Authorization<Guid>();
            return await CreateAuthorizedAsync(request, tenantId, definitionId, optionIds, createdAt, token);
        }, cancellationToken);
        if (response.IsSuccess)
            await _cache.RemoveByTagAsync(
                SessionCustomPropertyCache.ListsBySession(tenantId, request.DefinitionDto.EventSessionId),
                CancellationToken.None);
        return response;
    }

    private async Task<BaseCommandResponse<Guid>> CreateAuthorizedAsync(
        CreateEventSessionCustomPropertyDefinitionCommand request, Guid tenantId, Guid definitionId,
        Guid[] optionIds, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var session = await _sessionCustomPropertyRepository.GetParentForMutationAsync(
            request.DefinitionDto.EventSessionId, cancellationToken);
        if (session is null || session.TenantId != tenantId)
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
            tenantId,
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
                    tenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            tenantId,
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
                    tenantId));
        }

        var dto = request.DefinitionDto;
        var definition = new EventSessionCustomPropertyDefinition
        {
            Id = definitionId,
            CreatedAt = createdAt.UtcDateTime,
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
            TenantId = tenantId,
            InstantiatedAt = createdAt,
            CreatedBy = _currentUserService.UserId,
            UpdatedBy = _currentUserService.UserId
        };

        var options = CreateOptionEntities(request.DefinitionDto.Options, definition.Id, optionIds, createdAt.UtcDateTime);
        var defaultOption = options.SingleOrDefault(x => x.IsDefault);

        definition = await _sessionCustomPropertyRepository.CreateWithOptions(
            definition, options, defaultOption?.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return BaseCommandResponse.Success(definition.Id, "Event session custom property definition created successfully.");
    }

    private List<EventSessionCustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<DTOs.EventSessionCustomProperty.CreateEventSessionCustomPropertyOptionDto> optionDtos,
        Guid definitionId, Guid[] optionIds, DateTime createdAt)
    {
        return optionDtos
            .Select((optionDto, index) => new EventSessionCustomPropertyOption
            {
                Id = optionIds[index],
                CreatedAt = createdAt,
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
