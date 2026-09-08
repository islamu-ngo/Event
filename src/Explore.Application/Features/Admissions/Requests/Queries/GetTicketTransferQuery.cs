using Explore.Application.DTOs.Admissions;
using MediatR;

namespace Explore.Application.Features.Admissions.Requests.Queries;

public sealed record GetTicketTransferQuery(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId,
    string? CapabilityToken) :
    IRequest<TicketTransferDto?>;
