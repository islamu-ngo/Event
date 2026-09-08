using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Explore.Domain;

namespace Explore.Application.Features.Notifications.Handlers;

internal static class NotificationPreferenceMatrixMapper
{
    public static NotificationPreferenceMatrixDto Map(
        Guid tenantId,
        Guid userId,
        string scope,
        Guid? organizationId,
        Guid? groupId,
        IReadOnlyList<NotificationPreferenceCategory> categories,
        IReadOnlyList<NotificationPreferenceChannel> channels,
        IReadOnlyList<NotificationPreferenceDecision> decisions)
    {
        return new NotificationPreferenceMatrixDto
        {
            TenantId = tenantId,
            UserId = userId,
            OrganizationId = organizationId,
            GroupId = groupId,
            Scope = scope,
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
