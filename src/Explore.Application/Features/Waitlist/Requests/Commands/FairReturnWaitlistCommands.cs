using Explore.Application.DTOs.Waitlist;
using MediatR;

namespace Explore.Application.Features.Waitlist.Requests.Commands;

public sealed record JoinFairReturnWaitlistCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId) :
    IRequest<FairReturnWaitlistDto?>;

public sealed record LeaveFairReturnWaitlistCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId) :
    IRequest<FairReturnWaitlistDto?>;

public sealed record AcceptFairReturnOfferCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    Guid OfferId) :
    IRequest<FairReturnWaitlistDto?>;

public sealed record WithdrawFairReturnSupplyCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    Guid SupplyId) :
    IRequest<FairReturnWaitlistDto?>;
