using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorSubscription.Validators;
using Explore.Application.Features.ActorSubscriptions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Handlers.Commands;

public class SubscribeToActorCommandHandler : IRequestHandler<SubscribeToActorCommand, BaseCommandResponse<Guid>>
{
    private readonly IActorSubscriptionRepository _actorSubscriptionRepository;
    private readonly IActorRepository _actorRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public SubscribeToActorCommandHandler(
        IActorSubscriptionRepository actorSubscriptionRepository,
        IActorRepository actorRepository,
        ITenantUserRepository tenantUserRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _actorSubscriptionRepository = actorSubscriptionRepository;
        _actorRepository = actorRepository;
        _tenantUserRepository = tenantUserRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(SubscribeToActorCommand request, CancellationToken cancellationToken)
    {
        var validator = new SubscribeToActorDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Subscription, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure("Actor subscription failed.", validationResult.Errors.Select(error => error.ErrorMessage).ToList());
        }

        var tenantUser = await GetActiveCurrentTenantUserAsync(cancellationToken);
        if (tenantUser is null)
        {
            return Failure("Actor subscription failed.", ["An active tenant-local user is required before subscribing."]);
        }

        var targetActor = await _actorRepository.GetLocallyDiscoverableSubscriptionTargetAsync(
            _tenantContext.TenantId,
            request.Subscription.TargetActorId,
            cancellationToken);
        if (targetActor is null)
        {
            return Failure("Actor subscription failed.", ["Target actor must be an organization or group in the current tenant."]);
        }

        if (tenantUser.ActorId == targetActor.Id)
        {
            return Failure("Actor subscription failed.", ["Users cannot subscribe to their own actor."]);
        }

        var subscription = await _actorSubscriptionRepository.GetBySubscriberAndTargetAsync(
            _tenantContext.TenantId,
            tenantUser.Id,
            targetActor.Id,
            trackChanges: true,
            cancellationToken);

        if (subscription is null)
        {
            subscription = new ActorSubscription
            {
                TenantId = _tenantContext.TenantId,
                Tenant = null!,
                SubscriberTenantUserId = tenantUser.Id,
                SubscriberTenantUser = null!,
                SubscriberUserId = tenantUser.UserId,
                SubscriberUser = null!,
                TargetActorId = targetActor.Id,
                TargetActor = null!,
                TargetActorTypeId = targetActor.ActorTypeId,
                TargetActorType = null!,
                StatusId = (int)ActorSubscriptionStatusEnum.Active,
                Status = null!,
                NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All,
                NotificationLevel = null!,
                SubscribedAt = DateTime.UtcNow
            };

            subscription = await _actorSubscriptionRepository.Create(subscription);
        }
        else if (subscription.StatusId == (int)ActorSubscriptionStatusEnum.Blocked)
        {
            return Failure("Actor subscription failed.", ["Subscription is administratively blocked."]);
        }
        else if (subscription.StatusId != (int)ActorSubscriptionStatusEnum.Active)
        {
            subscription.StatusId = (int)ActorSubscriptionStatusEnum.Active;
            subscription.NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All;
            subscription.SubscribedAt = DateTime.UtcNow;
            subscription.UnsubscribedAt = null;
            subscription.SubscriberUserId = tenantUser.UserId;
            subscription.TargetActorTypeId = targetActor.ActorTypeId;
            await _actorSubscriptionRepository.Update(subscription);
        }

        return BaseCommandResponse.Success(subscription.Id, "Actor subscription is active.");
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

    private static BaseCommandResponse<Guid> Failure(string message, List<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
