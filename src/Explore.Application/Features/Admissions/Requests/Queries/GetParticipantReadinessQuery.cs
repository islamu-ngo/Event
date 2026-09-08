using Explore.Application.DTOs.Admissions;
using MediatR;

namespace Explore.Application.Features.Admissions.Requests.Queries;

public sealed record GetParticipantReadinessQuery(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid ParticipantId,
    Guid RegistrationTicketAssignmentId,
    string? CapabilityToken) :
    IRequest<ParticipantReadinessDto?>;
