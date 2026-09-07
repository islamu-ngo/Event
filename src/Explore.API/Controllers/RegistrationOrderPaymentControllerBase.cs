using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

public abstract class RegistrationOrderPaymentControllerBase(IMediator mediator) : ControllerBase
{
    protected const string CapabilityHeader = "X-Registration-Order-Capability";
    protected const string IdempotencyKeyHeader = "Idempotency-Key";
    protected IMediator Mediator { get; } = mediator;

    private static readonly ApiValidationProblemDescriptor PaymentValidationProblem = new(
        "registrationPayment", "Registration payment request failed", "Registration payment request failed.");
    private static readonly ApiNotFoundProblemDescriptor PaymentNotFoundProblem = new(
        "Registration payment not found", "Registration payment was not found.");
    private protected static readonly CommandFailurePolicy PaymentFailures = CommandFailurePolicy
        .ValidatedBy(PaymentValidationProblem)
        .NotFound(PaymentNotFoundProblem, "registration_order_not_found")
        .Conflict("Payment action unavailable", "The requested payment action is not available.", "not_payable", "payment_retry_not_available", "payment_acceptance_required", "payment_acceptance_stale", "payment_risk_review_required", "payment_review_required", "payment_ceiling_exceeded", "paid_sale_stopped")
        .Unavailable(
            "Payment temporarily unavailable",
            "Payment could not be started.",
            "payment_start_failed",
            "payment_readiness_unavailable",
            "payment_organizer_unavailable",
            "payment_configuration_unavailable",
            "payment_connection_unavailable",
            "payment_acceptance_unavailable",
            "payment_policy_invalid",
            "payment_policy_unavailable",
            "payment_operator_inactive",
            "paid_sale_control_uninitialized",
            "payment_activation_invalid",
            "payment_activation_unavailable");
    private protected static readonly CommandFailurePolicy RefundFailures = CommandFailurePolicy
        .ValidatedBy(PaymentValidationProblem)
        .NotFound(PaymentNotFoundProblem, "registration_order_not_found")
        .Conflict(
            "Refund unavailable",
            "The requested refund is not available.",
            "refund_payment_not_captured",
            "refund_capacity_exceeded",
            "refund_open_dispute",
            "refund_authority_mismatch");
    private protected static readonly CommandFailurePolicy MaterialChangeFailures = CommandFailurePolicy
        .ValidatedBy(PaymentValidationProblem)
        .NotFound(PaymentNotFoundProblem, "registration_order_not_found", "material_change_choice_not_found")
        .Conflict(
            "Material-change choice unavailable",
            "The requested material-change choice is not available.",
            "material_change_choice_invalid",
            "refund_payment_not_captured",
            "refund_capacity_exceeded",
            "refund_open_dispute",
            "refund_authority_mismatch");

    protected ActionResult<HalResource<RegistrationPaymentDto>> MapResult(
        RegistrationPaymentCommandResultDto result,
        Guid eventId,
        Guid orderId,
        bool guest)
    {
        if (!result.IsSuccess || result.Payment is null)
        {
            return PaymentFailures.Map(this, result);
        }

        return Ok(ToResource(result.Payment, eventId, orderId, guest));
    }

    protected HalResource<RegistrationPaymentDto> ToResource(
        RegistrationPaymentDto payment,
        Guid eventId,
        Guid orderId,
        bool guest)
    {
        string statusRoute = guest ? RouteNames.GetGuestRegistrationPayment : RouteNames.GetAuthenticatedRegistrationPayment;
        string retryRoute = guest ? RouteNames.RetryGuestRegistrationPayment : RouteNames.RetryAuthenticatedRegistrationPayment;
        var values = new { eventId, orderId };
        var resource = new HalResource<RegistrationPaymentDto>(payment)
            .WithLink(LinkRelations.Self, HalLink.Create(Url.Link(statusRoute, values)!));
        resource.WithLink(LinkRelations.PaymentStatus, HalLink.Create(Url.Link(statusRoute, values)!));

        if (payment.HostedRedirectAvailable)
        {
            resource.WithLink(LinkRelations.CheckoutRedirect, HalLink.CreateAction(
                $"bff/registration-payments/events/{eventId:D}/orders/{orderId:D}/checkout-ticket", HttpMethods.Post));
        }

        if (payment.RetryAvailable)
        {
            resource.WithLink(LinkRelations.RetryPayment, HalLink.CreateAction(Url.Link(retryRoute, values)!, HttpMethods.Post));
        }
        if (!guest && payment.BuyerRefundRequestAvailable)
        {
            resource.WithLink(LinkRelations.RequestRefund, HalLink.CreateAction(
                Url.Link(RouteNames.RequestAuthenticatedRegistrationRefund, values)!, HttpMethods.Post));
        }
        if (!guest && payment.MaterialChangeChoices.Any(choice => choice.StatusCode == "Pending"))
        {
            resource.WithLink(LinkRelations.RespondMaterialChange, HalLink.CreateAction(
                Url.Link(RouteNames.RespondAuthenticatedRegistrationMaterialChange, values)!, HttpMethods.Post));
        }

        return resource;
    }

    protected ActionResult<HalResource<RegistrationRefundDto>> MapRefundResult(
        RegistrationRefundCommandResultDto result,
        Guid eventId,
        Guid orderId,
        string statusRoute)
    {
        if (!result.IsSuccess || result.Refund is null)
        {
            return RefundFailures.Map(this, result);
        }

        var resource = new HalResource<RegistrationRefundDto>(result.Refund)
            .WithLink(LinkRelations.PaymentStatus, HalLink.Create(
                Url.Link(statusRoute, new { eventId, orderId })!));
        return Accepted(resource);
    }

    protected ActionResult<HalResource<RegistrationMaterialChangeChoiceDto>> MapMaterialChangeResult(
        RegistrationMaterialChangeChoiceCommandResultDto result,
        Guid eventId,
        Guid orderId)
    {
        if (!result.IsSuccess || result.Choice is null)
        {
            return MaterialChangeFailures.Map(this, result);
        }

        return Accepted(new HalResource<RegistrationMaterialChangeChoiceDto>(result.Choice)
            .WithLink(LinkRelations.PaymentStatus, HalLink.Create(
                Url.Link(RouteNames.GetAuthenticatedRegistrationPayment, new { eventId, orderId })!)));
    }

    protected ActionResult<RegistrationPaymentCheckoutTargetDto> TargetOrNotFound(RegistrationPaymentCheckoutTargetDto? target) =>
        target is null ? this.ToNotFoundProblem(PaymentNotFoundProblem) : Ok(target);

    protected ActionResult PaymentNotFoundResult() => this.ToNotFoundProblem(PaymentNotFoundProblem);
}
