using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public sealed class GetGroupNotificationPreferenceMatrixQueryHandler(
    INotificationChannelPreferenceRepository preferenceRepository,
    INotificationPreferenceResolver resolver,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetGroupNotificationPreferenceMatrixQuery, NotificationPreferenceMatrixDto>
{
    public async Task<NotificationPreferenceMatrixDto> Handle(
        GetGroupNotificationPreferenceMatrixQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        if (!userId.HasValue)
        {
            return new NotificationPreferenceMatrixDto
            {
                TenantId = tenantContext.TenantId,
                GroupId = request.GroupId,
                Scope = "group"
            };
        }

        var group = await groupTenantRepository.GetByGroupAndTenant(
            request.GroupId,
            tenantContext.TenantId,
            cancellationToken);
        var parentOrganization = group?.ParentOrganizationTenantId is { } parentOrganizationTenantId
            ? await organizationTenantRepository.GetById(parentOrganizationTenantId)
            : null;
        var organizationId = parentOrganization?.OrganizationId;
        var categories = await preferenceRepository.ListCategoriesAsync(cancellationToken);
        var channels = await preferenceRepository.ListChannelsAsync(cancellationToken);
        var resolveRequests = categories
            .SelectMany(category => channels.Select(channel => new NotificationPreferenceResolveRequest(
                tenantContext.TenantId,
                userId.Value,
                organizationId,
                request.GroupId,
                category.MasterCode,
                channel.MasterCode)))
            .ToArray();

        var decisions = await resolver.ResolveBatchAsync(resolveRequests, cancellationToken);
        return NotificationPreferenceMatrixMapper.Map(
            tenantContext.TenantId,
            userId.Value,
            "group",
            organizationId,
            request.GroupId,
            categories,
            channels,
            decisions);
    }
}
