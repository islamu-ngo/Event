using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public sealed class GetCurrentUserNotificationPreferenceMatrixQueryHandler(
    INotificationChannelPreferenceRepository preferenceRepository,
    INotificationPreferenceResolver resolver,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCurrentUserNotificationPreferenceMatrixQuery, NotificationPreferenceMatrixDto>
{
    public async Task<NotificationPreferenceMatrixDto> Handle(
        GetCurrentUserNotificationPreferenceMatrixQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        if (!userId.HasValue)
        {
            return new NotificationPreferenceMatrixDto { TenantId = tenantContext.TenantId };
        }

        var categories = await preferenceRepository.ListCategoriesAsync(cancellationToken);
        var channels = await preferenceRepository.ListChannelsAsync(cancellationToken);
        var resolveRequests = categories
            .SelectMany(category => channels.Select(channel => new NotificationPreferenceResolveRequest(
                tenantContext.TenantId,
                userId.Value,
                OrganizationId: null,
                GroupId: null,
                category.MasterCode,
                channel.MasterCode)))
            .ToArray();

        var decisions = await resolver.ResolveBatchAsync(resolveRequests, cancellationToken);

        return new NotificationPreferenceMatrixDto
        {
            TenantId = tenantContext.TenantId,
            UserId = userId.Value,
            Categories = categories.Select(category => new NotificationPreferenceCategoryDto
            {
                Code = category.MasterCode,
                Name = category.FullName,
                Description = category.Description,
                IsRequired = category.IsRequired,
                SortOrder = category.SortOrder
            }).ToArray(),
            Channels = channels.Select(channel => new NotificationPreferenceChannelDto
            {
                Code = channel.MasterCode,
                Name = channel.FullName,
                Description = channel.Description,
                SortOrder = channel.SortOrder
            }).ToArray(),
            Cells = decisions.Select(decision => new NotificationPreferenceCellDto
            {
                CategoryCode = decision.CategoryCode,
                ChannelCode = decision.ChannelCode,
                IsEnabled = decision.IsEnabled,
                IsEditable = !decision.IsRequired && !decision.IsLocked,
                IsRequired = decision.IsRequired,
                IsLocked = decision.IsLocked,
                IsMuted = decision.IsMuted,
                EffectiveSourceScope = decision.EffectiveSourceScope,
                LockReason = decision.LockReason
            }).ToArray(),
            Mute = new NotificationPreferenceMuteDto
            {
                IsMuted = decisions.Any(decision => decision.IsMuted),
                IsEditable = true
            }
        };
    }
}
