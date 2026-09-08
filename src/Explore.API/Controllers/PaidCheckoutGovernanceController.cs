using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Extensions;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Payments;
using Explore.Application.Features.PaidCheckoutGovernance.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/tenants/{tenantId:guid}/paid-checkout-governance")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class PaidCheckoutGovernanceController(
    IMediator mediator,
    IAuthorizationProvider authorization) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor SaleControlNotFoundProblem = new(
        "Paid-sale control not found",
        "The requested paid-sale control was not found.");

    private static readonly ApiValidationProblemDescriptor GovernanceValidationProblem = new(
        "paidCheckoutGovernance",
        "Paid Checkout governance request failed",
        "Paid Checkout governance request failed.");

    private static readonly CommandFailurePolicy GovernanceFailures = CommandFailurePolicy
        .ValidatedBy(GovernanceValidationProblem)
        .Conflict(
            "Paid Checkout governance action unavailable",
            "The requested paid Checkout governance transition is not available.",
            "paid_checkout_governance_invalid",
            "paid_checkout_resume_request_invalid",
            "paid_checkout_resume_review_invalid",
            "paid_checkout_review_request_invalid",
            "paid_checkout_review_decision_invalid");

    [HttpGet("sale-control", Name = RouteNames.GetPaidCheckoutSaleControl)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<PaidCheckoutSaleControlDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<PaidCheckoutSaleControlDto>>> GetSaleControl(
        Guid tenantId,
        [FromQuery] Guid? eventId,
        CancellationToken cancellationToken)
    {
        PaidCheckoutSaleControlDto? control = await mediator.Send(
            new GetPaidCheckoutSaleControlQuery(tenantId, eventId), cancellationToken);
        if (control is null) return this.ToNotFoundProblem(SaleControlNotFoundProblem);
        var resource = new HalResource<PaidCheckoutSaleControlDto>(control)
            .WithLink(LinkRelations.Self, HalLink.Create(Url.Link(
                RouteNames.GetPaidCheckoutSaleControl, new { tenantId, eventId })!));
        if (await CanMutateAsync(cancellationToken))
        {
            if (!control.IsStopped)
            {
                resource.WithLink(LinkRelations.StopPaidSales, HalLink.CreateAction(
                    Url.Link(RouteNames.StopPaidCheckoutSales, new { tenantId, eventId })!, HttpMethods.Post));
            }
            else if (!control.ResumeReviewPending)
            {
                resource.WithLink(LinkRelations.RequestPaidSalesResume, HalLink.CreateAction(
                    Url.Link(RouteNames.RequestPaidCheckoutResume, new { tenantId, eventId })!, HttpMethods.Post));
            }
            else
            {
                resource.WithLink(LinkRelations.ReviewPaidSalesResume, HalLink.CreateAction(
                    Url.Link(RouteNames.ReviewPaidCheckoutResume, new { tenantId, eventId })!, HttpMethods.Post));
            }
        }
        return Ok(resource);
    }

    [HttpPost("sale-control/stop", Name = RouteNames.StopPaidCheckoutSales)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> Stop(
        Guid tenantId, [FromQuery] Guid? eventId, [FromBody] PaidCheckoutSaleControlMutationDto body,
        CancellationToken cancellationToken) => ExecuteAsync(
            new StopPaidCheckoutSalesCommand(tenantId, eventId, body.ReasonCode), cancellationToken);

    [HttpPost("sale-control/resume-requests", Name = RouteNames.RequestPaidCheckoutResume)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> RequestResume(
        Guid tenantId, [FromQuery] Guid? eventId, [FromBody] PaidCheckoutSaleControlMutationDto body,
        CancellationToken cancellationToken) => ExecuteAsync(
            new RequestPaidCheckoutResumeCommand(tenantId, eventId, body.ReasonCode), cancellationToken);

    [HttpPost("sale-control/resume-reviews", Name = RouteNames.ReviewPaidCheckoutResume)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> ReviewResume(
        Guid tenantId, [FromQuery] Guid? eventId, [FromBody] PaidCheckoutResumeReviewDto body,
        CancellationToken cancellationToken) => ExecuteAsync(
            new ReviewPaidCheckoutResumeCommand(tenantId, eventId, body.Approved, body.ReasonCode), cancellationToken);

    [HttpPost("events/{eventId:guid}/reviews", Name = RouteNames.RequestPaidCheckoutReview)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> RequestReview(
        Guid tenantId, Guid eventId, [FromBody] RequestPaidCheckoutReviewDto body,
        CancellationToken cancellationToken) => ExecuteAsync(
            new RequestPaidCheckoutReviewCommand(tenantId, eventId, body.TriggerId, body.CurrencyCode,
                body.MaximumOrderAmountMinor, body.ReasonCode), cancellationToken);

    [HttpPost("reviews/{reviewId:guid}/decision", Name = RouteNames.DecidePaidCheckoutReview)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DecideReview(
        Guid tenantId, Guid reviewId, [FromBody] DecidePaidCheckoutReviewDto body,
        CancellationToken cancellationToken) => ExecuteAsync(
            new DecidePaidCheckoutReviewCommand(tenantId, reviewId, body.Approved, body.ReasonCode), cancellationToken);

    private async Task<ActionResult<BaseCommandResponse<Guid>>> ExecuteAsync(
        IRequest<BaseCommandResponse<Guid>> request,
        CancellationToken cancellationToken)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(request, cancellationToken);
        return response.IsSuccess ? Ok(response) : GovernanceFailures.Map(this, response);
    }

    private async Task<bool> CanMutateAsync(CancellationToken cancellationToken)
    {
        AuthorizationDecision decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.InstanceSetting,
            "paid-checkout-governance",
            AuthorizationActions.InstanceSettings.Update,
            AuthorizationScope.Empty,
            InstanceScopedAuthorizationFacts.Instance), cancellationToken);
        return decision.IsAllowed;
    }
}
