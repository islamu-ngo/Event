using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Services.Registration;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetRegistrationOrderQueryHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<GetRegistrationOrderQuery, RegistrationOrderDto?>
{
    public Task<RegistrationOrderDto?> Handle(GetRegistrationOrderQuery request, CancellationToken cancellationToken) =>
        lifecycle.GetAsync(request.OrderId, tenant.TenantId, cancellationToken);
}

public sealed class GetEventRegistrationOrdersQueryHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<GetEventRegistrationOrdersQuery, IReadOnlyList<RegistrationOrderDto>>
{
    public Task<IReadOnlyList<RegistrationOrderDto>> Handle(
        GetEventRegistrationOrdersQuery request,
        CancellationToken cancellationToken) =>
        lifecycle.GetByEventAsync(request.EventId, tenant.TenantId, cancellationToken);
}
