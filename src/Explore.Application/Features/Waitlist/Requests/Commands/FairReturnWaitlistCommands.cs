using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Waitlist;

namespace Explore.Application.Features.Waitlist.Requests.Commands;

public sealed record JoinFairReturnWaitlistCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId) :
    ICommand<FairReturnWaitlistDto?>;

public sealed record LeaveFairReturnWaitlistCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId) :
    ICommand<FairReturnWaitlistDto?>;

public sealed record AcceptFairReturnOfferCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    Guid OfferId) :
    ICommand<FairReturnWaitlistDto?>;

public sealed record WithdrawFairReturnSupplyCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    Guid SupplyId) :
    ICommand<FairReturnWaitlistDto?>;

