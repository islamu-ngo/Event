using System.Collections.Immutable;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AdmissionTickets.Handlers;

public sealed class GetCurrentAdmissionTicketsQueryHandler(
    IAdmissionTicketAccountRepository repository,
    IAdmissionTicketPresentationResolver presentationResolver,
    ITenantContext tenantContext,
    IUserContext userContext) :
    IQueryHandler<GetCurrentAdmissionTicketsQuery, IReadOnlyList<AdmissionTicketDto>>
{
    public async Task<IReadOnlyList<AdmissionTicketDto>> QueryAsync(
        GetCurrentAdmissionTicketsQuery query,
        CancellationToken cancellationToken = default)
    {
        Guid accountUserId = userContext.GetRequiredUserId();
        IReadOnlyList<AdmissionTicket> tickets = await repository.ListCurrentAsync(
            tenantContext.TenantId,
            accountUserId,
            cancellationToken);
        var presentation = await presentationResolver.ResolveAsync(
            tenantContext.TenantId,
            tickets.Select(ticket => ticket.Id).ToArray(),
            cancellationToken);
        return tickets.Select(ticket => Map(
            ticket,
            presentation.GetValueOrDefault(
                ticket.Id,
                AdmissionTicketPresentation.Empty))).ToArray();
    }

    internal static AdmissionTicketDto Map(
        AdmissionTicket ticket,
        AdmissionTicketPresentation presentation) =>
        new(
            ticket.Id,
            ticket.Id,
            ticket.EventId,
            ((AdmissionTicketStatusEnum)ticket.AdmissionTicketStatusId)
                .ToString()
                .ToUpperInvariant(),
            ticket.DisplayReference,
            ticket.RegistrationOrderId,
            presentation.HolderDisplayName,
            presentation.TicketTypeName,
            ticket.CreatedAt,
            presentation.Entitlements
                .Select(entitlement => new AdmissionTicketEntitlementDto(
                    entitlement.ScopeCode,
                    entitlement.EventTitle,
                    entitlement.DayLabel,
                    entitlement.LocalDate,
                    entitlement.SessionTitle,
                    entitlement.IncludedQuantity))
                .ToImmutableArray());
}
public sealed class GetCurrentAdmissionTicketQueryHandler(
    IAdmissionTicketAccountRepository repository,
    IAdmissionTicketPresentationResolver presentationResolver,
    ITenantContext tenantContext,
    IUserContext userContext) :
    IQueryHandler<GetCurrentAdmissionTicketQuery, AdmissionTicketDto>
{
    public async Task<AdmissionTicketDto> QueryAsync(
        GetCurrentAdmissionTicketQuery query,
        CancellationToken cancellationToken = default)
    {
        AdmissionTicket? ticket = await repository.GetOwnedAsync(
            tenantContext.TenantId,
            userContext.GetRequiredUserId(),
            query.TicketId,
            cancellationToken);
        if (ticket is null)
        {
            return null!;
        }

        var presentation = await presentationResolver.ResolveAsync(
            tenantContext.TenantId,
            [ticket.Id],
            cancellationToken);
        return GetCurrentAdmissionTicketsQueryHandler.Map(
            ticket,
            presentation.GetValueOrDefault(
                ticket.Id,
                AdmissionTicketPresentation.Empty));
    }
}
