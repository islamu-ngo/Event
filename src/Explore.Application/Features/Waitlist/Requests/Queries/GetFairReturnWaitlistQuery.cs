using Explore.Application.DTOs.Waitlist;
using MediatR;

namespace Explore.Application.Features.Waitlist.Requests.Queries;

public sealed record GetFairReturnWaitlistQuery(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    string? CapabilityToken) :
    IRequest<FairReturnWaitlistDto?>;
