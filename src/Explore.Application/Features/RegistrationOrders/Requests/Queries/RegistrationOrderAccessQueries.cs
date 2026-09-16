using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetGuestRegistrationOrderQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IQuery<GuestRegistrationOrderDto?>;

public sealed record GetCurrentRegistrationOrderQuery(Guid OrderId)
    : IRequest<RegistrationOrderDto?>;

public sealed record GetGuestRegistrationOrderParticipantsQuery(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken) : IQuery<RegistrationOrderParticipantsDto?>;

public sealed record GetAuthenticatedRegistrationOrderParticipantsQuery(Guid EventId, Guid OrderId)
    : IQuery<RegistrationOrderParticipantsDto?>;

public sealed record GetGuestNativeRegistrationRequirementProgressQuery(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken) : IQuery<NativeRegistrationRequirementProgressCollectionDto?>;

public sealed record GetAuthenticatedNativeRegistrationRequirementProgressQuery(Guid EventId, Guid OrderId)
    : IRequest<NativeRegistrationRequirementProgressCollectionDto?>;
