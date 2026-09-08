using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Features.ActorSubscriptions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Handlers.Queries;

public class GetActorSubscriptionRequestHandler : IRequestHandler<GetActorSubscriptionRequest, ActorSubscriptionDto?>
{
    private readonly IActorSubscriptionRepository _actorSubscriptionRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public GetActorSubscriptionRequestHandler(
        IActorSubscriptionRepository actorSubscriptionRepository,
        ITenantUserRepository tenantUserRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _actorSubscriptionRepository = actorSubscriptionRepository;
        _tenantUserRepository = tenantUserRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<ActorSubscriptionDto?> Handle(GetActorSubscriptionRequest request, CancellationToken cancellationToken)
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

        return subscription is null ? null : _mapper.Map<ActorSubscriptionDto>(subscription);
    }

    private async Task<Domain.TenantUser?> GetCurrentTenantUserAsync(CancellationToken cancellationToken)
    {
        return _currentUserService.UserId is Guid userId
            ? await _tenantUserRepository.GetByTenantAndUserAsync(_tenantContext.TenantId, userId, cancellationToken)
            : null;
    }
}
