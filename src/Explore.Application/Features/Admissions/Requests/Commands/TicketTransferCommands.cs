using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Admissions;

namespace Explore.Application.Features.Admissions.Requests.Commands;

public sealed record OfferTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId) :
    ICommand<TicketTransferOfferDto?>;

public sealed record AcceptTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId,
    Guid RecipientParticipantId,
    string? CapabilityToken) :
    ICommand<TicketTransferAcceptanceDto?>;

public sealed record CancelTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId) :
    ICommand<TicketTransferDto?>;

public sealed record CorrectTicketTransferCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId) :
    ICommand<TicketTransferAcceptanceDto?>;

public sealed record ReissueTransferredTicketCommand(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId) :
    ICommand<TicketTransferAcceptanceDto?>;
