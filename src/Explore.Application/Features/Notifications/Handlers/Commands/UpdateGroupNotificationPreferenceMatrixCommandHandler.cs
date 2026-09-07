using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Commands;

public sealed class UpdateGroupNotificationPreferenceMatrixCommandHandler(
    INotificationChannelPreferenceRepository preferenceRepository,
    INotificationPreferenceResolver resolver,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateGroupNotificationPreferenceMatrixCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateGroupNotificationPreferenceMatrixCommand request,
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
        var organizationId = parentOrganization?.OrganizationId;
        var validation = await ValidateCellsAsync(
            request.Cells,
            userId.Value,
            organizationId,
            request.GroupId,
            cancellationToken);

        if (validation.Errors.Count > 0)
        {
            return Failure("Notification preference update failed.", validation.Errors);
        }

        Guid lastId = Guid.Empty;
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            foreach (var cell in validation.Cells)
            {
                var preference = await preferenceRepository.UpsertGroupPreferenceAsync(
                    tenantContext.TenantId,
                    request.GroupId,
                    cell.CategoryId,
                    cell.ChannelId,
                    cell.IsEnabled,
                    token);
                lastId = preference.Id;
            }
        }, cancellationToken);

        return BaseCommandResponse.Success(lastId, "Group notification preferences updated.");
    }

    private async Task<(List<string> Errors, List<(int CategoryId, int ChannelId, bool IsEnabled)> Cells)> ValidateCellsAsync(
        IReadOnlyList<DTOs.Notification.UpdateNotificationPreferenceCellDto>? cells,
        Guid userId,
        Guid? organizationId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        if (cells is not { Count: > 0 })
        {
            return (["At least one preference cell is required."], []);
        }

        var categories = (await preferenceRepository.ListCategoriesAsync(cancellationToken))
            .ToDictionary(category => Normalize(category.MasterCode), StringComparer.Ordinal);
        var channels = (await preferenceRepository.ListChannelsAsync(cancellationToken))
            .ToDictionary(channel => Normalize(channel.MasterCode), StringComparer.Ordinal);
        var errors = new List<string>();
        var validated = new List<(int CategoryId, int ChannelId, bool IsEnabled)>();

        foreach (var cell in cells)
        {
            var categoryCode = Normalize(cell.CategoryCode);
            var channelCode = Normalize(cell.ChannelCode);

            if (!categories.TryGetValue(categoryCode, out var category))
            {
                errors.Add($"Unknown notification preference category '{cell.CategoryCode}'.");
                continue;
            }

            if (!channels.TryGetValue(channelCode, out var channel))
            {
                errors.Add($"Unknown notification preference channel '{cell.ChannelCode}'.");
                continue;
            }

            if (category.IsRequired && !cell.IsEnabled)
            {
                errors.Add($"Category '{category.MasterCode}' is required and cannot be disabled.");
                continue;
            }

            var decision = await resolver.ResolveAsync(new NotificationPreferenceResolveRequest(
                tenantContext.TenantId,
                userId,
                organizationId,
                groupId,
                category.MasterCode,
                channel.MasterCode), cancellationToken);

            if (decision.IsLocked && decision.EffectiveSourceScope != "Group")
            {
                errors.Add($"Preference '{category.MasterCode}/{channel.MasterCode}' is locked by {decision.EffectiveSourceScope}.");
                continue;
            }

            validated.Add((category.Id, channel.Id, cell.IsEnabled));
        }

        return (errors, validated);
    }

    private static BaseCommandResponse<Guid> Failure(string message, List<string>? errors = null) =>
        BaseCommandResponse.Validation<Guid>(errors ?? [message], message);

    private static string Normalize(string code) => code.Trim().ToLowerInvariant();
}
