using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyDefinition.Validators;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;

public class CreateCustomPropertyDefinitionCommandHandler : IRequestHandler<CreateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly ICustomPropertyGovernancePolicy _customPropertyGovernancePolicy;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCustomPropertyDefinitionCommandHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        ICustomPropertyGovernancePolicy customPropertyGovernancePolicy,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IMapper mapper,
        HybridCache cache,
        IUnitOfWork unitOfWork)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
        _customPropertyGovernancePolicy = customPropertyGovernancePolicy;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateCustomPropertyDefinitionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.DefinitionDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Custom-property definition creation failed.");
        }

        var governance = _customPropertyGovernancePolicy.EvaluateDefinition(request.DefinitionDto.Namespace, request.DefinitionDto.Key);
        if (!governance.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                governance.Errors,
                "Custom-property definition creation failed.");
        }

        if (await _customPropertyDefinitionRepository.ExistsScopedMachineKey(
                _tenantContext.TenantId,
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
            _tenantContext.TenantId,
            cancellationToken);
        var currentDefinitionCount = await _customPropertyDefinitionRepository.CountDefinitionsForScope(
            _tenantContext.TenantId,
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
                    _tenantContext.TenantId));
        }

        var maxOptions = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
            _tenantContext.TenantId,
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
                    _tenantContext.TenantId));
        }

        var definition = _mapper.Map<CustomPropertyDefinition>(request.DefinitionDto);
        definition.TenantId = _tenantContext.TenantId;
        definition.Namespace = governance.NormalizedNamespace;
        definition.Key = governance.NormalizedKey;
        definition.CreatedBy = _currentUserService.UserId;
        definition.UpdatedBy = _currentUserService.UserId;

        var options = CreateOptionEntities(request.DefinitionDto.Options, definition.Id);
        var defaultOption = options.SingleOrDefault(x => x.IsDefault);

        definition = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _customPropertyDefinitionRepository.CreateWithOptions(definition, options, defaultOption?.Id, ct),
            cancellationToken);

        await _cache.RemoveAsync(GetListCacheKey(request.DefinitionDto.EntityTypeName, 1, PaginatedResult<object>.DefaultPageSize), cancellationToken);

        return BaseCommandResponse.Success(definition.Id, "Custom-property definition created successfully.");
    }

    private List<CustomPropertyOption> CreateOptionEntities(IReadOnlyCollection<DTOs.CustomPropertyDefinition.CreateCustomPropertyOptionDto> optionDtos, Guid definitionId)
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

    private static string GetListCacheKey(EntityTypeName entityTypeName, int pageNumber, int pageSize)
    {
        return $"custom-property-definitions:list:{entityTypeName}:{pageNumber}:{pageSize}";
    }
}
