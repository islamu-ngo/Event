using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Waitlist;

namespace Explore.Application.Features.Waitlist.Requests.Queries;

public sealed record GetFairReturnWaitlistQuery(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    string? CapabilityToken) :
    IQuery<FairReturnWaitlistDto?>;

