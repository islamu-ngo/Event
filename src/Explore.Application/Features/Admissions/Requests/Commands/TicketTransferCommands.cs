using Explore.Application.DTOs.Admissions;
using MediatR;

namespace Explore.Application.Features.Admissions.Requests.Commands;

public sealed record OfferTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId) :
    IRequest<TicketTransferOfferDto?>;

public sealed record AcceptTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId,
    Guid RecipientParticipantId,
    string? CapabilityToken) :
    IRequest<TicketTransferAcceptanceDto?>;

public sealed record CancelTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId) :
    IRequest<TicketTransferDto?>;

public sealed record CorrectTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId) :
    IRequest<TicketTransferAcceptanceDto?>;

public sealed record ReissueTransferredTicketCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId) :
    IRequest<TicketTransferAcceptanceDto?>;
