using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetGuestRegistrationPaymentQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IQuery<RegistrationPaymentDto?>, IGuestRegistrationOrderAccessCommand;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.View)]
public sealed record GetAuthenticatedRegistrationPaymentQuery(Guid EventId, Guid OrderId)
    : IQuery<RegistrationPaymentDto?>, IAuthenticatedRegistrationPaymentSecureRequest;

public sealed record GetGuestPaidOrderAcceptanceQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IQuery<PaidOrderAcceptanceDisclosureDto?>, IGuestRegistrationOrderAccessCommand;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.Continue)]
public sealed record GetAuthenticatedPaidOrderAcceptanceQuery(Guid EventId, Guid OrderId)
    : IQuery<PaidOrderAcceptanceDisclosureDto?>, IAuthenticatedRegistrationPaymentSecureRequest;

public sealed record GetGuestRegistrationPaymentCheckoutTargetQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IQuery<RegistrationPaymentCheckoutTargetDto?>, IGuestRegistrationOrderAccessCommand;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.View)]
public sealed record GetAuthenticatedRegistrationPaymentCheckoutTargetQuery(Guid EventId, Guid OrderId)
    : IQuery<RegistrationPaymentCheckoutTargetDto?>, IAuthenticatedRegistrationPaymentSecureRequest;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record GetStudioRegistrationPaymentQuery(Guid EventId, Guid OrderId)
    : IQuery<RegistrationPaymentDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
