using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public sealed class GetOrganizationNotificationPreferenceMatrixQueryHandler(
    INotificationChannelPreferenceRepository preferenceRepository,
    INotificationPreferenceResolver resolver,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetOrganizationNotificationPreferenceMatrixQuery, NotificationPreferenceMatrixDto>
{
    public async Task<NotificationPreferenceMatrixDto> Handle(
        GetOrganizationNotificationPreferenceMatrixQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        if (!userId.HasValue)
        {
            return new NotificationPreferenceMatrixDto
            {
                TenantId = tenantContext.TenantId,
                OrganizationId = request.OrganizationId,
                Scope = "organization"
            };
        }

        var categories = await preferenceRepository.ListCategoriesAsync(cancellationToken);
        var channels = await preferenceRepository.ListChannelsAsync(cancellationToken);
        var resolveRequests = categories
            .SelectMany(category => channels.Select(channel => new NotificationPreferenceResolveRequest(
                tenantContext.TenantId,
                userId.Value,
                request.OrganizationId,
                GroupId: null,
                category.MasterCode,
                channel.MasterCode)))
            .ToArray();

        var decisions = await resolver.ResolveBatchAsync(resolveRequests, cancellationToken);
        return NotificationPreferenceMatrixMapper.Map(
            tenantContext.TenantId,
            userId.Value,
            "organization",
            request.OrganizationId,
            groupId: null,
            categories,
            channels,
            decisions);
    }
}
