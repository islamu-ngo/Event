// ABOUTME: Defines the purpose-restricted post-confirmation guest status query and authorized DTO mapping.
// ABOUTME: Keeps capability formatting redacted and tenant authority outside caller-supplied request data.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Queries;

public sealed record GetGuestRegistrationStatusQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<GuestRegistrationStatusDto?>
{
    public override string ToString() => "GetGuestRegistrationStatusQuery { Redacted = true }";
}

public sealed class GetGuestRegistrationStatusQueryHandler(
    IGuestRegistrationCapabilityRepository guestRegistrations,
    IEventRepository events,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<GetGuestRegistrationStatusQuery, GuestRegistrationStatusDto?>
{
    public async Task<GuestRegistrationStatusDto?> Handle(GetGuestRegistrationStatusQuery request, CancellationToken cancellationToken)
    {
        if (!GuestRegistrationStatusAccessGuard.IsWellFormed(
            tenant.TenantId, request.EventId, request.OrderId, request.CapabilityToken))
        {
            return null;
        }

        try
        {
            var result = await unitOfWork.ExecuteSerializableAsync<GuestRegistrationStatusDto?>(async token =>
            {
                var snapshot = await GuestRegistrationStatusAccessGuard.GetAsync(
                    guestRegistrations, events, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                    request.CapabilityToken!, timeProvider, token);
                if (snapshot is not { } authorized || authorized.Deadline <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    return null;
                }

                return new GuestRegistrationStatusDto(
                    authorized.Event.Id, authorized.Order.Id, authorized.Event.EventStatusId,
                    authorized.Order.RegistrationOrderStatusId, authorized.Order.ConfirmedAt!.Value,
                    authorized.Order.CancelledAt, authorized.Event.LastSessionEndUtc,
                    new DateTimeOffset(authorized.Deadline, TimeSpan.Zero));
            }, cancellationToken);

            // Commit/disposal may wait after mapping. Never disclose using time sampled before a wait.
            return result is not null && timeProvider.GetUtcNow() < result.StatusAccessUntil ? result : null;
        }
        catch (GuestRegistrationStatusAccessGuard.GuestStatusPromiseExpiredException)
        {
            return null;
        }
    }
}
