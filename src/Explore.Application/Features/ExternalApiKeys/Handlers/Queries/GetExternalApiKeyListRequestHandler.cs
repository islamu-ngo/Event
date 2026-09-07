using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Lookups;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Queries;

public class GetExternalApiKeyListRequestHandler : IRequestHandler<GetExternalApiKeyListRequest, List<ExternalApiKeyListDto>>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;

    public GetExternalApiKeyListRequestHandler(
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

    public async Task<List<ExternalApiKeyListDto>> Handle(GetExternalApiKeyListRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();
        var visibleKeys = new List<Explore.Domain.ExternalApiKey>();

        visibleKeys.AddRange(await _externalApiKeyRepository.GetByOwner(ExternalApiKeyOwnerType.User, currentUserId, cancellationToken));

        var organizationIds = await _organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(currentUserId, PermissionCodes.OrganizationManage, cancellationToken);

        if (organizationIds.Count > 0)
        {
            visibleKeys.AddRange(await _externalApiKeyRepository.GetByOwners(ExternalApiKeyOwnerType.Organization, organizationIds, cancellationToken));
        }

        var groupIds = await _groupMemberRepository.GetGroupIdsWhereUserHasPermission(currentUserId, PermissionCodes.GroupManage, cancellationToken);

        if (groupIds.Count > 0)
        {
            visibleKeys.AddRange(await _externalApiKeyRepository.GetByOwners(ExternalApiKeyOwnerType.Group, groupIds, cancellationToken));
        }

        var tenantIds = await _adminContext.GetAdminTenantIdsAsync(cancellationToken);

        if (tenantIds.Count > 0)
        {
            visibleKeys.AddRange(await _externalApiKeyRepository.GetByOwners(ExternalApiKeyOwnerType.Tenant, tenantIds, cancellationToken));
        }

        if (await _adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            visibleKeys.AddRange(await _externalApiKeyRepository.GetByOwnerIgnoringTenantFilter(
                ExternalApiKeyOwnerType.InstanceAdmin, currentUserId, cancellationToken));
        }

        return visibleKeys
            .OrderByDescending(key => key.CreatedAt)
            .Select(key => new ExternalApiKeyListDto
            {
                Id = key.Id,
                Name = key.Name,
                Description = key.Description,
                KeyId = key.KeyId,
                MaskedKeyId = MaskKeyId(key.KeyId),
                ExternalApiKeyOwnerTypeId = key.ExternalApiKeyOwnerTypeId,
                ExternalApiKeyOwnerTypeCode = NormalizedLookupMetadata.ExternalApiKeyOwnerType(key.ExternalApiKeyOwnerTypeId).Code,
                ExternalApiKeyOwnerTypeName = NormalizedLookupMetadata.ExternalApiKeyOwnerType(key.ExternalApiKeyOwnerTypeId).Name,
                OwnerId = key.OwnerId,
                Scopes = SplitScopes(key.Scopes),
                ExternalApiKeyStatusId = key.ExternalApiKeyStatusId,
                ExternalApiKeyStatusCode = NormalizedLookupMetadata.ExternalApiKeyStatus(key.ExternalApiKeyStatusId).Code,
                ExternalApiKeyStatusName = NormalizedLookupMetadata.ExternalApiKeyStatus(key.ExternalApiKeyStatusId).Name,
                ExpiresAt = key.ExpiresAt,
                LastUsedAt = key.LastUsedAt,
                ExternalApiKeyCreditPeriodId = key.ExternalApiKeyCreditPeriodId,
                ExternalApiKeyCreditPeriodCode = NormalizedLookupMetadata.ExternalApiKeyCreditPeriod(key.ExternalApiKeyCreditPeriodId).Code,
                ExternalApiKeyCreditPeriodName = NormalizedLookupMetadata.ExternalApiKeyCreditPeriod(key.ExternalApiKeyCreditPeriodId).Name,
                CreditLimit = key.CreditLimit,
                MaxRolloverCredits = key.MaxRolloverCredits
            })
            .ToList();
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
