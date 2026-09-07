using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetGuestRegistrationPaymentQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<RegistrationPaymentDto?>, IGuestRegistrationOrderAccessCommand;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.View)]
public sealed record GetAuthenticatedRegistrationPaymentQuery(Guid EventId, Guid OrderId)
    : IRequest<RegistrationPaymentDto?>, IAuthenticatedRegistrationPaymentSecureRequest;

public sealed record GetGuestPaidOrderAcceptanceQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<PaidOrderAcceptanceDisclosureDto?>, IGuestRegistrationOrderAccessCommand;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.Continue)]
public sealed record GetAuthenticatedPaidOrderAcceptanceQuery(Guid EventId, Guid OrderId)
    : IRequest<PaidOrderAcceptanceDisclosureDto?>, IAuthenticatedRegistrationPaymentSecureRequest;

public sealed record GetGuestRegistrationPaymentCheckoutTargetQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<RegistrationPaymentCheckoutTargetDto?>, IGuestRegistrationOrderAccessCommand;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.View)]
public sealed record GetAuthenticatedRegistrationPaymentCheckoutTargetQuery(Guid EventId, Guid OrderId)
    : IRequest<RegistrationPaymentCheckoutTargetDto?>, IAuthenticatedRegistrationPaymentSecureRequest;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record GetStudioRegistrationPaymentQuery(Guid EventId, Guid OrderId)
    : IRequest<RegistrationPaymentDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
