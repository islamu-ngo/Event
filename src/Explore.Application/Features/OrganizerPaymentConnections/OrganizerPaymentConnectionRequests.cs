using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.OrganizerPaymentConnections;

namespace Explore.Application.Features.OrganizerPaymentConnections;

public sealed record GetOrganizerPaymentConnectionQuery(
    Guid TenantId,
    Guid OrganizerActorId,
    Guid ConnectionId) : IQuery<OrganizerPaymentConnectionDto?>;

public sealed record ListOrganizerPaymentConnectionsQuery(Guid TenantId, Guid OrganizerActorId)
    : IQuery<IReadOnlyList<OrganizerPaymentConnectionDto>>;
