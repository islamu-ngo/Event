using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Notifications;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Services;

public sealed class ActorSubscriptionService(
    IActorSubscriptionClient apiClient,
    ILogger<ActorSubscriptionService> logger) : IActorSubscriptionService
{
    public async Task<ActorSubscriptionDto?> GetSubscriptionAsync(Guid targetActorId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.GetActorSubscriptionByActorAsync(
                targetActorId,
                cancellationToken: cancellationToken);

            return response.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] API error fetching actor subscription state: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] Error fetching actor subscription state");
            return null;
        }
    }

    public async Task<ActorSubscriptionCommandResult> SubscribeAsync(Guid targetActorId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.SubscribeToActorAsync(
                new SubscribeToActorDto { TargetActorId = targetActorId },
                cancellationToken: cancellationToken);

            return ToResult(response, "Subscription failed.");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] API error subscribing to actor: {StatusCode}", ex.StatusCode);
            return ActorSubscriptionCommandResult.Failed("Could not update subscription. Please try again.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] Error subscribing to actor");
            return ActorSubscriptionCommandResult.Failed("Could not update subscription. Please try again.");
        }
    }

    public async Task<ActorSubscriptionCommandResult> UnsubscribeAsync(
        Guid targetActorId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.UnsubscribeFromActorAsync(
                targetActorId,
                new UnsubscribeFromActorDto
                {
                    TargetActorId = targetActorId,
                    ExpectedConcurrencyStamp = expectedConcurrencyStamp
                },
                cancellationToken: cancellationToken);

            return ToResult(response, "Unsubscribe failed.");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] API error unsubscribing from actor: {StatusCode}", ex.StatusCode);
            return ActorSubscriptionCommandResult.Failed("Could not update subscription. Please refresh and try again.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] Error unsubscribing from actor");
            return ActorSubscriptionCommandResult.Failed("Could not update subscription. Please refresh and try again.");
        }
    }

    public async Task<ActorSubscriptionCommandResult> UpdateNotificationLevelAsync(
        Guid targetActorId,
        int notificationLevelId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.UpdateActorSubscriptionNotificationLevelAsync(
            targetActorId,
            new UpdateActorSubscriptionNotificationLevelDto
            {
                NotificationLevel = new UpdateActorSubscriptionNotificationLevelValueDto
                {
                    Id = notificationLevelId
                },
                ExpectedConcurrencyStamp = expectedConcurrencyStamp
            },
                cancellationToken: cancellationToken);

            return ToResult(response, "Notification preference update failed.");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] API error updating actor subscription notification level: {StatusCode}", ex.StatusCode);
            return ActorSubscriptionCommandResult.Failed("Could not update notification preference. Please refresh and try again.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ACTOR SUBSCRIPTION SERVICE] Error updating actor subscription notification level");
            return ActorSubscriptionCommandResult.Failed("Could not update notification preference. Please refresh and try again.");
        }
    }

    private static ActorSubscriptionCommandResult ToResult(BaseCommandResponseOfGuid? response, string fallbackMessage)
    {
        if (response?.Success == true)
        {
            return new ActorSubscriptionCommandResult(true, response.Id, response.Message);
        }

        var errors = response?.Errors?.ToList() ?? [];
        return new ActorSubscriptionCommandResult(false, response?.Id, response?.Message ?? fallbackMessage, errors);
    }
}
