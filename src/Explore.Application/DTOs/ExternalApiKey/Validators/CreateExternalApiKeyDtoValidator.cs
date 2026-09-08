using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.ExternalApiKeys;
using Explore.Application.Lookups;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.ExternalApiKey.Validators;

internal class CreateExternalApiKeyDtoValidator : AbstractValidator<CreateExternalApiKeyDto>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IGroupRepository _groupRepository;

    public CreateExternalApiKeyDtoValidator(
        IExternalApiKeyRepository externalApiKeyRepository,
        IOrganizationRepository organizationRepository,
        IGroupRepository groupRepository,
        Guid currentUserId,
        Guid? tenantId)
    {
        _externalApiKeyRepository = externalApiKeyRepository;
        _organizationRepository = organizationRepository;
        _groupRepository = groupRepository;

        RuleFor(x => x.ExternalApiKeyOwnerTypeId)
            .Must(NormalizedLookupMetadata.IsExternalApiKeyOwnerTypeId)
            .WithMessage("Invalid external API key owner type.");

        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("API key name is required.")
            .MaximumLength(ExternalApiKeyInputValidation.NameMaxLength).WithMessage("API key name cannot exceed 200 characters.")
            .Must(ExternalApiKeyInputValidation.DoesNotContainControlCharacters).WithMessage("API key name must not contain control characters.")
            .MustAsync((dto, name, cancellationToken) => NameIsUniqueAsync(
                dto,
                currentUserId,
                tenantId,
                ExternalApiKeyInputValidation.NormalizeRequiredText(name),
                cancellationToken))
            .WithMessage("An API key with the same name already exists for this owner.");

        RuleFor(x => x.Description)
            .MaximumLength(ExternalApiKeyInputValidation.DescriptionMaxLength).WithMessage("API key description cannot exceed 1000 characters.")
            .Must(ExternalApiKeyInputValidation.DoesNotContainControlCharacters).WithMessage("API key description must not contain control characters.");

        RuleFor(x => x.Scopes)
            .NotEmpty().WithMessage("At least one scope is required.")
            .Must(scopes => scopes.All(scope => !string.IsNullOrWhiteSpace(scope)))
            .WithMessage("Scopes cannot contain empty values.")
            .Must(scopes => ExternalApiKeyScopes.AreAllValid(scopes))
            .WithMessage((dto, _) => $"Invalid scopes: {string.Join(", ", ExternalApiKeyScopes.GetInvalid(dto.Scopes))}.")
            .Must((dto, scopes) => ExternalApiKeyScopeCeiling.AreWithinCeiling(ToOwnerType(dto.ExternalApiKeyOwnerTypeId), scopes))
            .WithMessage((dto, _) => $"Scopes exceed ceiling for {ToOwnerType(dto.ExternalApiKeyOwnerTypeId)}: {string.Join(", ", ExternalApiKeyScopeCeiling.GetExceeding(ToOwnerType(dto.ExternalApiKeyOwnerTypeId), dto.Scopes))}.");

        When(x => ToOwnerType(x.ExternalApiKeyOwnerTypeId) == ExternalApiKeyOwnerType.Organization, () =>
        {
            RuleFor(x => x.OrganizationId)
                .NotEmpty().WithMessage("Organization ID is required for organization-owned API keys.")
                .MustAsync(OrganizationExistsAsync)
                .WithMessage("Organization does not exist.");
        });

        When(x => ToOwnerType(x.ExternalApiKeyOwnerTypeId) == ExternalApiKeyOwnerType.Group, () =>
        {
            RuleFor(x => x.GroupId)
                .NotEmpty().WithMessage("Group ID is required for group-owned API keys.")
                .MustAsync(GroupExistsAsync)
                .WithMessage("Group does not exist.");
        });
    }

    private async Task<bool> NameIsUniqueAsync(
        CreateExternalApiKeyDto dto, Guid currentUserId, Guid? tenantId, string name, CancellationToken cancellationToken)
    {
        var ownerId = ResolveOwnerId(dto, currentUserId, tenantId);

        if (!ownerId.HasValue)
        {
            return false;
        }

        // InstanceAdmin keys have TenantId=null, so the standard tenant query filter
        // would never match them. Use the tenant-filter-bypass variant.
        var ownerType = ToOwnerType(dto.ExternalApiKeyOwnerTypeId);

        if (ownerType == ExternalApiKeyOwnerType.InstanceAdmin)
        {
            return !await _externalApiKeyRepository.ExistsByOwnerAndNameIgnoringTenantFilter(ownerType, ownerId.Value, name, cancellationToken);
        }

        return !await _externalApiKeyRepository.ExistsByOwnerAndName(ownerType, ownerId.Value, name, cancellationToken);
    }

    private static Guid? ResolveOwnerId(CreateExternalApiKeyDto dto, Guid currentUserId, Guid? tenantId)
    {
        return ToOwnerType(dto.ExternalApiKeyOwnerTypeId) switch
        {
            ExternalApiKeyOwnerType.Organization => dto.OrganizationId,
            ExternalApiKeyOwnerType.Group => dto.GroupId,
            ExternalApiKeyOwnerType.Tenant => tenantId,
            _ => currentUserId // User and InstanceAdmin both use current user's ID
        };
    }

    private static ExternalApiKeyOwnerType ToOwnerType(int ownerTypeId)
    {
        return (ExternalApiKeyOwnerType)ownerTypeId;
    }

    private async Task<bool> OrganizationExistsAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        return organizationId.HasValue && await _organizationRepository.Exists(organizationId.Value);
    }

    private async Task<bool> GroupExistsAsync(Guid? groupId, CancellationToken cancellationToken)
    {
        return groupId.HasValue && await _groupRepository.Exists(groupId.Value);
    }
}
