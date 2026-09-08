using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Lookups;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Queries;

public class GetExternalApiKeyDetailsRequestHandler : IRequestHandler<GetExternalApiKeyDetailsRequest, ExternalApiKeyListDto?>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;

    public GetExternalApiKeyDetailsRequestHandler(
        IExternalApiKeyRepository externalApiKeyRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        IAdminContext adminContext,
        IUserContext userContext)
    {
        _externalApiKeyRepository = externalApiKeyRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _groupMemberRepository = groupMemberRepository;
        _adminContext = adminContext;
        _userContext = userContext;
    }

    public async Task<ExternalApiKeyListDto?> Handle(GetExternalApiKeyDetailsRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();
        var externalApiKey = await _externalApiKeyRepository.GetByIdIgnoringTenantFilter(request.Id, cancellationToken);

        if (externalApiKey is null || !await CanManageAsync(externalApiKey, currentUserId, cancellationToken))
        {
            return null;
        }

        return new ExternalApiKeyListDto
        {
            Id = externalApiKey.Id,
            Name = externalApiKey.Name,
            Description = externalApiKey.Description,
            KeyId = externalApiKey.KeyId,
            MaskedKeyId = MaskKeyId(externalApiKey.KeyId),
            ExternalApiKeyOwnerTypeId = externalApiKey.ExternalApiKeyOwnerTypeId,
            ExternalApiKeyOwnerTypeCode = NormalizedLookupMetadata.ExternalApiKeyOwnerType(externalApiKey.ExternalApiKeyOwnerTypeId).Code,
            ExternalApiKeyOwnerTypeName = NormalizedLookupMetadata.ExternalApiKeyOwnerType(externalApiKey.ExternalApiKeyOwnerTypeId).Name,
            OwnerId = externalApiKey.OwnerId,
            Scopes = SplitScopes(externalApiKey.Scopes),
            ExternalApiKeyStatusId = externalApiKey.ExternalApiKeyStatusId,
            ExternalApiKeyStatusCode = NormalizedLookupMetadata.ExternalApiKeyStatus(externalApiKey.ExternalApiKeyStatusId).Code,
            ExternalApiKeyStatusName = NormalizedLookupMetadata.ExternalApiKeyStatus(externalApiKey.ExternalApiKeyStatusId).Name,
            ExpiresAt = externalApiKey.ExpiresAt,
            LastUsedAt = externalApiKey.LastUsedAt,
            ExternalApiKeyCreditPeriodId = externalApiKey.ExternalApiKeyCreditPeriodId,
            ExternalApiKeyCreditPeriodCode = NormalizedLookupMetadata.ExternalApiKeyCreditPeriod(externalApiKey.ExternalApiKeyCreditPeriodId).Code,
            ExternalApiKeyCreditPeriodName = NormalizedLookupMetadata.ExternalApiKeyCreditPeriod(externalApiKey.ExternalApiKeyCreditPeriodId).Name,
            CreditLimit = externalApiKey.CreditLimit,
            MaxRolloverCredits = externalApiKey.MaxRolloverCredits
        };
    }

    private async Task<bool> CanManageAsync(Explore.Domain.ExternalApiKey externalApiKey, Guid currentUserId, CancellationToken cancellationToken)
    {
        return externalApiKey.OwnerType switch
        {
            ExternalApiKeyOwnerType.User => externalApiKey.OwnerId == currentUserId,
            ExternalApiKeyOwnerType.Organization => await _organizationMemberRepository.HasPermissionInOrganization(
                externalApiKey.OwnerId, currentUserId, PermissionCodes.OrganizationManage),
            ExternalApiKeyOwnerType.Group => await _groupMemberRepository.HasPermissionInGroup(
                externalApiKey.OwnerId, currentUserId, PermissionCodes.GroupManage),
            ExternalApiKeyOwnerType.Tenant => await _adminContext.IsTenantAdminAsync(externalApiKey.TenantId!.Value, cancellationToken),
            ExternalApiKeyOwnerType.InstanceAdmin => await _adminContext.IsInstanceAdminAsync(cancellationToken),
            _ => false
        };
    }

    private static List<string> SplitScopes(string scopes)
    {
        return scopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string MaskKeyId(string keyId)
    {
        return keyId.Length > 4
            ? string.Concat("".PadLeft(keyId.Length - 4, '\u2022'), keyId.AsSpan(keyId.Length - 4))
            : keyId;
    }
}
