using Explore.Application.DTOs.RegistrationOrders;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetRegistrationOrderParticipantsQuery(Guid RegistrationOrderId)
    : IRequest<RegistrationOrderParticipantsDto?>;
