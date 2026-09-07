using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Commands;

public class RevokeExternalApiKeyCommandHandler : IRequestHandler<RevokeExternalApiKeyCommand, bool>
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

    public async Task<bool> Handle(RevokeExternalApiKeyCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();
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
            "External API key {KeyId} revoked for tenant {TenantId} by user {UserId}.",
            externalApiKey.KeyId,
            externalApiKey.TenantId?.ToString() ?? "platform",
            currentUserId);

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
