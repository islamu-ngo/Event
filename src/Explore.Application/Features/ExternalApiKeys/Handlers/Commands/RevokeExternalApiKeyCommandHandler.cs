using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Commands;

public class RevokeExternalApiKeyCommandHandler : ICommandHandler<RevokeExternalApiKeyCommand, bool>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<RevokeExternalApiKeyCommandHandler> _logger;

    public RevokeExternalApiKeyCommandHandler(
        IExternalApiKeyRepository externalApiKeyRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        IAdminContext adminContext,
        IUserContext userContext,
        BusinessMetrics metrics,
        ILogger<RevokeExternalApiKeyCommandHandler> logger)
    {
        _externalApiKeyRepository = externalApiKeyRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _groupMemberRepository = groupMemberRepository;
        _adminContext = adminContext;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Looks up the exact key across tenant scope, then requires owner-specific management authority before idempotent revocation.
    /// </summary>
    public async Task<bool> ExecuteAsync(RevokeExternalApiKeyCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated)
            return false;
        var resolvedUserId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (resolvedUserId is not Guid currentUserId || currentUserId == Guid.Empty)
            return false;
        var externalApiKey = await _externalApiKeyRepository.GetByIdIgnoringTenantFilter(request.Id, cancellationToken);

        if (externalApiKey is null || !await CanManageAsync(externalApiKey, currentUserId, cancellationToken))
        {
            return false;
        }

        if (externalApiKey.ExternalApiKeyStatusId == (int)ExternalApiKeyStatusEnum.Revoked)
        {
            return true;
        }

        externalApiKey.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
        externalApiKey.UpdatedAt = DateTime.UtcNow;
        externalApiKey.UpdatedBy = currentUserId;
        await _externalApiKeyRepository.Update(externalApiKey);

        _metrics.RecordExternalApiKeyRevoked(
            externalApiKey.TenantId?.ToString() ?? "platform",
            externalApiKey.OwnerType.ToString());

        _logger.LogInformation(
            "External API key revoked. OwnerType: {OwnerType}.",
            externalApiKey.OwnerType);

        return true;
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
}
