
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers;

namespace Explore.Application.Features.RegistrationOrders.Queries;

public sealed record GetGuestRegistrationStatusQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IQuery<GuestRegistrationStatusDto?>
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
    : IQueryHandler<GetGuestRegistrationStatusQuery, GuestRegistrationStatusDto?>
{
    public async Task<GuestRegistrationStatusDto?> QueryAsync(GetGuestRegistrationStatusQuery query, CancellationToken cancellationToken = default)
    {
        if (!GuestRegistrationStatusAccessGuard.IsWellFormed(
            tenant.TenantId, query.EventId, query.OrderId, query.CapabilityToken))
        {
            return null;
        }

        try
        {
            var result = await unitOfWork.ExecuteSerializableAsync<GuestRegistrationStatusDto?>(async token =>
            {
                var snapshot = await GuestRegistrationStatusAccessGuard.GetAsync(
                    guestRegistrations, events, capabilities, tenant.TenantId, query.EventId, query.OrderId,
                    query.CapabilityToken!, timeProvider, token);
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
