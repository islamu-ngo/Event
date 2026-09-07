namespace Explore.Application.Features.EmailUnsubscribe.Handlers.Commands;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailUnsubscribe.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class UnsubscribeFromEmailCategoryCommandHandler(
    IUserNotificationPreferenceRepository preferenceRepository,
    ILogger<UnsubscribeFromEmailCategoryCommandHandler> logger)
    : IRequestHandler<UnsubscribeFromEmailCategoryCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UnsubscribeFromEmailCategoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!NotificationPreferenceCategories.IsKnown(request.Category))
        {
            return BaseCommandResponse.Failure<Guid>(
                "unknown_notification_category",
                "Unknown notification category.",
                ["Unknown notification category."]);
        }

        var normalizedCategory = NotificationPreferenceCategories.Normalize(request.Category);
        var now = DateTime.UtcNow;
        var existing = await preferenceRepository.GetByUserAndCategory(
            request.TenantId,
            request.UserId,
            normalizedCategory);

        if (existing is not null)
        {
            existing.IsEnabled = false;
            existing.UpdatedAt = now;
            existing.UpdatedBy = request.UserId;
            await preferenceRepository.Update(existing);
        }
        else
        {
            await preferenceRepository.Create(new UserNotificationPreference
            {
                TenantId = request.TenantId,
                Tenant = null!,
                UserId = request.UserId,
                Category = normalizedCategory,
                IsEnabled = false,
                CreatedAt = now,
                CreatedBy = request.UserId,
                UpdatedAt = now,
                UpdatedBy = request.UserId
            });
        }

        logger.LogInformation(
            "Email category unsubscribed. TenantId={TenantId} UserId={UserId} Category={Category}",
            request.TenantId,
            request.UserId,
            normalizedCategory);

        return BaseCommandResponse.Success(request.UserId, "Email notification preference updated.");
    }
}
