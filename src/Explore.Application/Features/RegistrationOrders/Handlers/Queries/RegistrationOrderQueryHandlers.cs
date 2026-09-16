using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Services.Registration;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetRegistrationOrderQueryHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IQueryHandler<GetRegistrationOrderQuery, RegistrationOrderDto?>
{
    public Task<RegistrationOrderDto?> QueryAsync(GetRegistrationOrderQuery request, CancellationToken cancellationToken) =>
        lifecycle.GetAsync(request.OrderId, tenant.TenantId, cancellationToken);
}

public sealed class GetEventRegistrationOrdersQueryHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IQueryHandler<GetEventRegistrationOrdersQuery, IReadOnlyList<RegistrationOrderDto>>
{
    public Task<IReadOnlyList<RegistrationOrderDto>> QueryAsync(
        GetEventRegistrationOrdersQuery request,
        CancellationToken cancellationToken) =>
        lifecycle.GetByEventAsync(request.EventId, tenant.TenantId, cancellationToken);
}
