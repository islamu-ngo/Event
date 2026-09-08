using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Features.ActorSubscriptions.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ActorSubscriptions.Handlers.Queries;

public class GetActorSubscriptionsRequestHandler : IRequestHandler<GetActorSubscriptionsRequest, PaginatedResult<ActorSubscriptionListDto>>
{
    private readonly IActorSubscriptionRepository _actorSubscriptionRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public GetActorSubscriptionsRequestHandler(
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

    public async Task<PaginatedResult<ActorSubscriptionListDto>> Handle(GetActorSubscriptionsRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<ActorSubscriptionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var tenantUser = await GetCurrentTenantUserAsync(cancellationToken);
        if (tenantUser is null)
        {
            return PaginatedResult<ActorSubscriptionListDto>.Create([], 0, pageNumber, pageSize);
        }

        var (items, totalCount) = await _actorSubscriptionRepository.GetBySubscriberPagedAsync(
            _tenantContext.TenantId,
            tenantUser.Id,
            pageNumber,
            pageSize,
            cancellationToken);

        var dtos = _mapper.Map<List<ActorSubscriptionListDto>>(items);
        return PaginatedResult<ActorSubscriptionListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }

    private async Task<Domain.TenantUser?> GetCurrentTenantUserAsync(CancellationToken cancellationToken)
    {
        return _currentUserService.UserId is Guid userId
            ? await _tenantUserRepository.GetByTenantAndUserAsync(_tenantContext.TenantId, userId, cancellationToken)
            : null;
    }
}
