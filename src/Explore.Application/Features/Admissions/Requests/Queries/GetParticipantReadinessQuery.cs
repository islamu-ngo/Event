using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Admissions;

namespace Explore.Application.Features.Admissions.Requests.Queries;

public sealed record GetParticipantReadinessQuery(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid ParticipantId,
    Guid RegistrationTicketAssignmentId,
    string? CapabilityToken) :
    IQuery<ParticipantReadinessDto?>;
