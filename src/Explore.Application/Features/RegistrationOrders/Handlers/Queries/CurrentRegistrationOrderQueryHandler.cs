using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetCurrentRegistrationOrderQueryHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant,
    ICurrentUserService currentUser)
    : IRequestHandler<GetCurrentRegistrationOrderQuery, RegistrationOrderDto?>
{
    public async Task<RegistrationOrderDto?> Handle(GetCurrentRegistrationOrderQuery request, CancellationToken cancellationToken)
    {
        return await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory,
            currentUser,
            tenant.TenantId,
            request.OrderId,
            cancellationToken) is null
            ? null
            : await lifecycle.GetAsync(request.OrderId, tenant.TenantId, cancellationToken);
    }
}

public sealed class GetAuthenticatedNativeRegistrationRequirementProgressQueryHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ISender sender)
    : IRequestHandler<GetAuthenticatedNativeRegistrationRequirementProgressQuery, NativeRegistrationRequirementProgressCollectionDto?>
{
    public async Task<NativeRegistrationRequirementProgressCollectionDto?> Handle(
        GetAuthenticatedNativeRegistrationRequirementProgressQuery request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
                inventory,
                currentUser,
                tenant.TenantId,
                request.EventId,
                request.OrderId,
                cancellationToken) is null)
        {
            return null;
        }

        return await sender.Send(new GetNativeRegistrationRequirementProgressQuery(
            tenant.TenantId, request.EventId, request.OrderId), cancellationToken);
    }
}
