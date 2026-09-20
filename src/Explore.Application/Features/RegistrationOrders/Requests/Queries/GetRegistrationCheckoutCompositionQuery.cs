using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetRegistrationCheckoutCompositionQuery(Guid EventId)
    : IQuery<RegistrationCheckoutCompositionDto?>;
