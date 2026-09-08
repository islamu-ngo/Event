using Explore.Application.DTOs.EventAddOns;
using MediatR;

namespace Explore.Application.Features.EventAddOns.Requests.Queries;

public sealed record GetEventAddOnCatalogQuery(
    Guid EventId,
    bool ManagementView) : IRequest<EventAddOnCatalogDto?>;

public sealed record GetRegistrationOrderAddOnsQuery(
    Guid EventId,
    Guid RegistrationOrderId,
    string? Capability) : IRequest<RegistrationOrderAddOnSummaryDto?>;
