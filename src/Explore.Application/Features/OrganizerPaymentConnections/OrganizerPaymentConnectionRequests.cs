using Explore.Application.DTOs.OrganizerPaymentConnections;
using MediatR;

namespace Explore.Application.Features.OrganizerPaymentConnections;

public sealed record GetOrganizerPaymentConnectionQuery(
    Guid TenantId,
    Guid OrganizerActorId,
    Guid ConnectionId) : IRequest<OrganizerPaymentConnectionDto?>;

public sealed record ListOrganizerPaymentConnectionsQuery(Guid TenantId, Guid OrganizerActorId)
    : IRequest<IReadOnlyList<OrganizerPaymentConnectionDto>>;
