using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorSubscription.Validators;
using Explore.Application.Features.ActorSubscriptions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Handlers.Commands;

public class UpdateActorSubscriptionNotificationLevelCommandHandler : IRequestHandler<UpdateActorSubscriptionNotificationLevelCommand, BaseCommandResponse<Guid>>
{
    private readonly IActorSubscriptionRepository _actorSubscriptionRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public UpdateActorSubscriptionNotificationLevelCommandHandler(
        IActorSubscriptionRepository actorSubscriptionRepository,
        ITenantUserRepository tenantUserRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _actorSubscriptionRepository = actorSubscriptionRepository;
        _tenantUserRepository = tenantUserRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateActorSubscriptionNotificationLevelCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateActorSubscriptionNotificationLevelDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Patch, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure("Actor subscription update failed.", validationResult.Errors.Select(error => error.ErrorMessage).ToList());
        }

        var tenantUser = await GetActiveCurrentTenantUserAsync(cancellationToken);
        if (tenantUser is null)
        {
            return Failure("Actor subscription update failed.", ["An active tenant-local user is required before updating subscriptions."]);
        }

        var subscription = await _actorSubscriptionRepository.GetBySubscriberAndTargetAsync(
            _tenantContext.TenantId,
            tenantUser.Id,
            request.TargetActorId,
            trackChanges: true,
            cancellationToken);

        if (subscription is null)
        {
            return Failure(
                "Actor subscription update failed.",
                ["Subscription was not found."],
                "actor_subscription_not_found");
        }

        if (subscription.StatusId != (int)ActorSubscriptionStatusEnum.Active)
        {
            return Failure("Actor subscription update failed.", ["Only active subscriptions can change notification level."]);
        }

        if (subscription.ConcurrencyStamp != request.Patch.ExpectedConcurrencyStamp)
        {
            return Failure("Actor subscription update failed.", ["Subscription changed since it was loaded."]);
        }

        subscription.NotificationLevelId = request.Patch.NotificationLevel!.Id;
        await _actorSubscriptionRepository.Update(subscription);

        return BaseCommandResponse.Success(
            subscription.Id,
            "Actor subscription notification level updated.");
    }

    private async Task<TenantUser?> GetActiveCurrentTenantUserAsync(CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not Guid userId)
        {
            return null;
        }

        var tenantUser = await _tenantUserRepository.GetByTenantAndUserAsync(_tenantContext.TenantId, userId, cancellationToken);
        return tenantUser is not null
            && tenantUser.StatusId == (int)TenantUserStatusEnum.Active
            && !tenantUser.IsDeleted
                ? tenantUser
                : null;
    }

    private static BaseCommandResponse<Guid> Failure(
        string message,
        List<string> errors,
        string? failureCode = null) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors);
}
