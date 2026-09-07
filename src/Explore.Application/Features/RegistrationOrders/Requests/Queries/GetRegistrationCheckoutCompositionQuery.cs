using Explore.Application.DTOs.RegistrationOrders;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetRegistrationCheckoutCompositionQuery(Guid EventId)
    : IRequest<RegistrationCheckoutCompositionDto?>;
