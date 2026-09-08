using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Commands;

public sealed class SetGroupNotificationPreferenceMuteCommandHandler(
    INotificationPreferenceProfileRepository profileRepository,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SetGroupNotificationPreferenceMuteCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        SetGroupNotificationPreferenceMuteCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        if (!userId.HasValue)
        {
            return Failure("User not authenticated.");
        }

        var group = await groupTenantRepository.GetByGroupAndTenant(
            request.GroupId,
            tenantContext.TenantId,
            cancellationToken);
        var parentOrganization = group?.ParentOrganizationTenantId is { } parentOrganizationTenantId
            ? await organizationTenantRepository.GetById(parentOrganizationTenantId)
            : null;
        var profiles = await profileRepository.ListForUserContextAsync(
            tenantContext.TenantId,
            userId.Value,
            parentOrganization?.OrganizationId,
            request.GroupId,
            cancellationToken);
        var lockedProfile = profiles
            .Where(profile => profile.IsLocked)
            .OrderBy(profile => profile.ScopeId)
            .FirstOrDefault();

        if (lockedProfile is not null && lockedProfile.ScopeId != (int)ConfigurationScopeEnum.Group)
        {
            return Failure($"Notification mute is locked by {ScopeName(lockedProfile.ScopeId)} scope.");
        }

        Guid profileId = Guid.Empty;
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var profile = await profileRepository.UpsertGroupMuteAsync(
                tenantContext.TenantId,
                request.GroupId,
                request.IsMuted,
                token);
            profileId = profile.Id;
        }, cancellationToken);

        return BaseCommandResponse.Success(
            profileId,
            request.IsMuted
                ? "Group non-essential notifications muted."
                : "Group non-essential notification mute disabled.");
    }

    private static BaseCommandResponse<Guid> Failure(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);

    private static string ScopeName(int scopeId) => Enum.IsDefined(typeof(ConfigurationScopeEnum), scopeId)
        ? ((ConfigurationScopeEnum)scopeId).ToString()
        : "Unknown";
}
