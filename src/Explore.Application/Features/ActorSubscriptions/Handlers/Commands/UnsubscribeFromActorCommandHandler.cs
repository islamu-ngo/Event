using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorSubscription.Validators;
using Explore.Application.Features.ActorSubscriptions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Handlers.Commands;

public class UnsubscribeFromActorCommandHandler : IRequestHandler<UnsubscribeFromActorCommand, BaseCommandResponse<Guid>>
{
    private readonly IActorSubscriptionRepository _actorSubscriptionRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public UnsubscribeFromActorCommandHandler(
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

    public async Task<BaseCommandResponse<Guid>> Handle(UnsubscribeFromActorCommand request, CancellationToken cancellationToken)
    {
        var validator = new UnsubscribeFromActorDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Subscription, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure("Actor unsubscribe failed.", validationResult.Errors.Select(error => error.ErrorMessage).ToList());
        }

        var tenantUser = await GetActiveCurrentTenantUserAsync(cancellationToken);
        if (tenantUser is null)
        {
            return Failure("Actor unsubscribe failed.", ["An active tenant-local user is required before unsubscribing."]);
        }

        var subscription = await _actorSubscriptionRepository.GetBySubscriberAndTargetAsync(
            _tenantContext.TenantId,
            tenantUser.Id,
            request.Subscription.TargetActorId,
            trackChanges: true,
            cancellationToken);

        if (subscription is null)
        {
            return BaseCommandResponse.Success(
                Guid.Empty,
                "Actor subscription is already unsubscribed.");
        }

        if (subscription.ConcurrencyStamp != request.Subscription.ExpectedConcurrencyStamp)
        {
            return Failure("Actor unsubscribe failed.", ["Subscription changed since it was loaded."]);
        }

        if (subscription.StatusId != (int)ActorSubscriptionStatusEnum.Unsubscribed)
        {
            subscription.StatusId = (int)ActorSubscriptionStatusEnum.Unsubscribed;
            subscription.UnsubscribedAt = DateTime.UtcNow;
            await _actorSubscriptionRepository.Update(subscription);
        }

        return BaseCommandResponse.Success(subscription.Id, "Actor subscription is unsubscribed.");
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
