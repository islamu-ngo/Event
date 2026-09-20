using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetRegistrationOrderParticipantsQuery(Guid RegistrationOrderId)
    : IQuery<RegistrationOrderParticipantsDto?>;
