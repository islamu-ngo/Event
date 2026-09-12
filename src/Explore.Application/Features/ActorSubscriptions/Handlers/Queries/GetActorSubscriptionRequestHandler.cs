using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Features.ActorSubscriptions.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ActorSubscriptions.Handlers.Queries;

public class GetActorSubscriptionRequestHandler : IQueryHandler<GetActorSubscriptionRequest, ActorSubscriptionDto?>
{
    private readonly IActorSubscriptionRepository _actorSubscriptionRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public GetActorSubscriptionRequestHandler(
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

    public async Task<ActorSubscriptionDto?> QueryAsync(GetActorSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var tenantUser = await GetCurrentTenantUserAsync(cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var subscription = await _actorSubscriptionRepository.GetDiscoverableBySubscriberAndTargetAsync(
            _tenantContext.TenantId,
            tenantUser.Id,
            request.TargetActorId,
            cancellationToken);

        return subscription is null ? null : ActorSubscriptionMapper.ToDetail(subscription);
    }

    private async Task<Domain.TenantUser?> GetCurrentTenantUserAsync(CancellationToken cancellationToken)
    {
        return _currentUserService.UserId is Guid userId
            ? await _tenantUserRepository.GetByTenantAndUserAsync(_tenantContext.TenantId, userId, cancellationToken)
            : null;
    }
}
