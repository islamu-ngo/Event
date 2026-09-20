using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

public interface IAuthenticatedRegistrationPaymentSecureRequest
    : IAuthenticatedRegistrationOrderAccessCommand, ISecureRequest
{
    string? ISecureRequest.ResourceId => OrderId == Guid.Empty ? null : OrderId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new RegistrationOrderAuthorizationFacts(Guid.Empty, EventId, null);
}

public sealed record StartGuestRegistrationPaymentCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    PaidOrderAcceptanceAcknowledgementDto? Acceptance)
    : ICommand<RegistrationPaymentCommandResultDto>, IGuestRegistrationOrderAccessCommand;

public sealed record RetryGuestRegistrationPaymentCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : ICommand<RegistrationPaymentCommandResultDto>, IGuestRegistrationOrderAccessCommand;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.Continue)]
public sealed record StartAuthenticatedRegistrationPaymentCommand(
    Guid EventId,
    Guid OrderId,
    PaidOrderAcceptanceAcknowledgementDto? Acceptance)
    : ICommand<RegistrationPaymentCommandResultDto>, IAuthenticatedRegistrationPaymentSecureRequest;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.Continue)]
public sealed record RetryAuthenticatedRegistrationPaymentCommand(Guid EventId, Guid OrderId)
    : ICommand<RegistrationPaymentCommandResultDto>, IAuthenticatedRegistrationPaymentSecureRequest;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.RequestRefund)]
public sealed record RequestAuthenticatedRegistrationRefundCommand(
    Guid EventId,
    Guid OrderId,
    RegistrationRefundRequestDto Request,
    string IdempotencyKey)
    : ICommand<RegistrationRefundCommandResultDto>, IAuthenticatedRegistrationPaymentSecureRequest;

[AuthorizeResource(ResourceKinds.RegistrationOrder, AuthorizationActions.RegistrationOrders.RespondMaterialChange)]
public sealed record RespondAuthenticatedRegistrationMaterialChangeCommand(
    Guid EventId,
    Guid OrderId,
    RegistrationMaterialChangeChoiceRequestDto Request)
    : ICommand<RegistrationMaterialChangeChoiceCommandResultDto>, IAuthenticatedRegistrationPaymentSecureRequest;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record CreateStudioRegistrationRefundCommand(
    Guid EventId,
    Guid OrderId,
    RegistrationRefundRequestDto Request,
    string IdempotencyKey)
    : ICommand<RegistrationRefundCommandResultDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record RetryStudioRegistrationRefundCommand(
    Guid EventId,
    Guid OrderId,
    Guid RefundAttemptId)
    : ICommand<RegistrationRefundCommandResultDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
