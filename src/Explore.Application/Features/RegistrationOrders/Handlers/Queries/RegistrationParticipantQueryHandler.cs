using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetRegistrationOrderParticipantsQueryHandler(
    IRegistrationInventoryRepository inventory,
    IEventTicketCatalogRepository catalogs,
    IRegistrationParticipantRepository participants,
    ITenantContext tenant)
    : IRequestHandler<GetRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>
{
    public async Task<RegistrationOrderParticipantsDto?> Handle(
        GetRegistrationOrderParticipantsQuery request,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(request.RegistrationOrderId, tenant.TenantId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        EventTicketCatalogVersion? catalog = await catalogs.GetOrderCatalogAsync(
            order.TicketCatalogVersionId, order.EventId, order.TenantId, cancellationToken);
        if (catalog is null)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, EventTicketType> ticketTypes = catalog.TicketTypes.ToDictionary(ticketType => ticketType.Id);

        IReadOnlyList<RegistrationParticipant> participantRows =
            await participants.GetParticipantsByOrderAsync(order.Id, order.TenantId, cancellationToken);
        IReadOnlyList<RegistrationTicketAssignment> assignmentRows =
            await participants.GetAssignmentsWithParticipantsByOrderAsync(order.Id, order.TenantId, cancellationToken);
        return new RegistrationOrderParticipantsDto(
            order.Id,
            order.Lines.Select(line =>
            {
                EventTicketType ticketType = ticketTypes[line.TicketTypeId];
                var mode = (ParticipantDataCollectionModeEnum)ticketType.ParticipantDataCollectionModeId;
                return new RegistrationParticipantOrderLineDto(
                    line.Id,
                    line.TicketTypeNameSnapshot,
                    line.Quantity,
                    ticketType.ParticipantDataCollectionModeId,
                    ticketType.ParticipantDataCollectionMode?.MasterCode ?? ModeCode(mode),
                    ticketType.RequiresGuardian);
            }).ToArray(),
            participantRows.Select(participant => new RegistrationParticipantDto(
                participant.Id,
                participant.RegistrationOrderId,
                participant.ParticipantTypeId,
                participant.GuardianParticipantId,
                participant.Pii?.DisplayName,
                participant.Pii?.Email,
                participant.Pii?.Phone)).ToArray(),
            assignmentRows.Select(assignment => new RegistrationTicketAssignmentDto(
                assignment.Id,
                assignment.RegistrationOrderLineId,
                assignment.Ordinal,
                assignment.ParticipantId,
                assignment.AssignmentStatusId,
                assignment.AssignmentDeadline)).ToArray());
    }

    private static string ModeCode(ParticipantDataCollectionModeEnum mode) => mode switch
    {
        ParticipantDataCollectionModeEnum.None => "NONE",
        ParticipantDataCollectionModeEnum.LeadBookerOnly => "LEAD_BOOKER_ONLY",
        ParticipantDataCollectionModeEnum.PerTicketOptional => "PER_TICKET_OPTIONAL",
        ParticipantDataCollectionModeEnum.PerTicketRequired => "PER_TICKET_REQUIRED",
        ParticipantDataCollectionModeEnum.DeferredAssignment => "DEFERRED_ASSIGNMENT",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
