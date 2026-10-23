using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
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
    IPrivateEventManagementReader privateManagement)
    : IQueryHandler<GetEventRegistrationOrdersQuery, IReadOnlyList<RegistrationOrderDto>>
{
    public async Task<IReadOnlyList<RegistrationOrderDto>> QueryAsync(
        GetEventRegistrationOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var graph = await privateManagement.ReadAsync(new(PrivateEventManagementReadKind.Registrations, request.EventId), cancellationToken);
        return graph.Registrations.OrderByDescending(order => order.CreatedAt).Select(order => RegistrationOrderDto.From(order)).ToArray();
    }
}
