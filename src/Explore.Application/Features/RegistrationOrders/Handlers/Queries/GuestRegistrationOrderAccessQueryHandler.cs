using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetGuestRegistrationOrderQueryHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider)
    : IRequestHandler<GetGuestRegistrationOrderQuery, GuestRegistrationOrderDto?>
{
    public async Task<GuestRegistrationOrderDto?> Handle(GetGuestRegistrationOrderQuery request, CancellationToken cancellationToken)
    {
        var command = new ContinueGuestRegistrationOrderCommand(request.EventId, request.OrderId, request.CapabilityToken);
        if (!(await new GuestRegistrationOrderAccessCommandValidator<ContinueGuestRegistrationOrderCommand>()
                .ValidateAsync(command, cancellationToken)).IsValid ||
            await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory,
                capabilities,
                tenant.TenantId,
                request.EventId,
                request.OrderId,
                request.CapabilityToken,
                timeProvider,
                cancellationToken) is null)
        {
            return null;
        }

        RegistrationOrderDto? order = await lifecycle.GetAsync(request.OrderId, tenant.TenantId, cancellationToken);
        return order is null ? null : GuestRegistrationOrderDto.From(order);
    }
}

public sealed class GetGuestNativeRegistrationRequirementProgressQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<GetGuestNativeRegistrationRequirementProgressQuery, NativeRegistrationRequirementProgressCollectionDto?>
{
    public async Task<NativeRegistrationRequirementProgressCollectionDto?> Handle(
        GetGuestNativeRegistrationRequirementProgressQuery request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory,
                capabilities,
                tenant.TenantId,
                request.EventId,
                request.OrderId,
                request.CapabilityToken,
                timeProvider,
                cancellationToken) is null)
        {
            return null;
        }

        return await sender.Send(new GetNativeRegistrationRequirementProgressQuery(
            tenant.TenantId, request.EventId, request.OrderId), cancellationToken);
    }
}
