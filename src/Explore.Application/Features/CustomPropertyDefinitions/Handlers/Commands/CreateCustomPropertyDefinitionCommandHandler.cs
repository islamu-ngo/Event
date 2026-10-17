using Explore.Application.Authorization;
using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyDefinition.Validators;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;

public class CreateCustomPropertyDefinitionCommandHandler : ICommandHandler<CreateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly HybridCache _cache;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<CreateCustomPropertyDefinitionCommand> _authorization;
    private readonly ILogger<CreateCustomPropertyDefinitionCommandHandler> _logger;
    private readonly IHumanAdministrativeActionPreparation _preparation;

    public CreateCustomPropertyDefinitionCommandHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        HybridCache cache,
        ISerializableAttemptOwner owner,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<CreateCustomPropertyDefinitionCommand> authorization,
        ILogger<CreateCustomPropertyDefinitionCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
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

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        request = request with { DefinitionDto = request.DefinitionDto with { } };
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        var validator = new CreateCustomPropertyDefinitionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.DefinitionDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Custom-property definition creation failed.");
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
                CacheTags.CustomPropertyDefinitionListsByScope(tenantId, request.DefinitionDto.EntityTypeName),
                CancellationToken.None);
        return response;
    }

    private async Task<BaseCommandResponse<Guid>> CreateAuthorizedAsync(
        CreateCustomPropertyDefinitionCommand request, Guid tenantId, Guid definitionId,
        Guid[] optionIds, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var governance = _customPropertyGovernancePolicy.EvaluateDefinition(request.DefinitionDto.Namespace, request.DefinitionDto.Key);
        if (!governance.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                governance.Errors,
                "Custom-property definition creation failed.");
        }

        if (await _customPropertyDefinitionRepository.ExistsScopedMachineKey(
                tenantId,
                request.DefinitionDto.EntityTypeName,
                governance.NormalizedNamespace,
                governance.NormalizedKey))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["A custom-property definition with the same Namespace + Key already exists in this scope."],
                "Custom-property definition creation failed.");
        }

        var maxDefinitions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTenantPerEntityScope.Key,
            tenantId,
            cancellationToken);
        var currentDefinitionCount = await _customPropertyDefinitionRepository.CountDefinitionsForScope(
            tenantId,
            request.DefinitionDto.EntityTypeName,
            cancellationToken);
        if (currentDefinitionCount >= maxDefinitions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Custom-property definition creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerTenantPerEntityScope.Key,
                    maxDefinitions,
                    currentDefinitionCount,
                    currentDefinitionCount + 1,
                    "custom_property_definition_scope",
                    tenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            tenantId,
            cancellationToken);
        if (request.DefinitionDto.Options.Count > maxOptions)
        {
            return BaseCommandResponse.Quota<Guid>(
                "Custom-property definition creation failed.",
                new QuotaExceededDetails(
                    CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                    maxOptions,
                    null,
                    request.DefinitionDto.Options.Count,
                    "custom_property_definition_options",
                    tenantId));
        }

        var dto = request.DefinitionDto;
        var definition = new CustomPropertyDefinition
        {
            Id = definitionId,
            CreatedAt = createdAt.UtcDateTime,
            EntityTypeName = dto.EntityTypeName,
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
            CreatedBy = _currentUserService.UserId,
            UpdatedBy = _currentUserService.UserId
        };

        var options = CreateOptionEntities(request.DefinitionDto.Options, definition.Id, optionIds, createdAt.UtcDateTime);
        var defaultOption = options.SingleOrDefault(x => x.IsDefault);

        definition = await _customPropertyDefinitionRepository.CreateWithOptions(
            definition, options, defaultOption?.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return BaseCommandResponse.Success(definition.Id, "Custom-property definition created successfully.");
    }

    private List<CustomPropertyOption> CreateOptionEntities(
        IReadOnlyCollection<DTOs.CustomPropertyDefinition.CreateCustomPropertyOptionDto> optionDtos,
        Guid definitionId, Guid[] optionIds, DateTime createdAt)
    {
        return optionDtos
            .Select((optionDto, index) => new CustomPropertyOption
            {
                Id = optionIds[index],
                CreatedAt = createdAt,
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

}
