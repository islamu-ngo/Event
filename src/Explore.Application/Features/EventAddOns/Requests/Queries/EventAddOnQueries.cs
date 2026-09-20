using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAddOns;

namespace Explore.Application.Features.EventAddOns.Requests.Queries;

public sealed record GetEventAddOnCatalogQuery(
    Guid EventId,
    bool ManagementView) : IQuery<EventAddOnCatalogDto?>;

public sealed record GetRegistrationOrderAddOnsQuery(
    Guid EventId,
    Guid RegistrationOrderId,
    string? Capability) : IQuery<RegistrationOrderAddOnSummaryDto?>;
