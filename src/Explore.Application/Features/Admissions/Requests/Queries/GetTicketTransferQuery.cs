using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Admissions;

namespace Explore.Application.Features.Admissions.Requests.Queries;

public sealed record GetTicketTransferQuery(
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId,
    string? CapabilityToken) :
    IQuery<TicketTransferDto?>;
