// ABOUTME: Issues anonymous proof work bound to the exact intended guest-start business request and key.
// ABOUTME: Delegates tenant/event eligibility and durable issuance budgets without allocating orders or inventory.

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Middleware;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IO;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}")]
[ApiController]
public sealed class AnonymousRegistrationChallengeController(
    IMediator mediator, ITenantContext tenant, RecyclableMemoryStreamManager streams) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("anonymousRegistrationChallenge", "Invalid registration challenge", "The challenge request is invalid."))
        .NotFound(new("Registration unavailable", "Anonymous registration is unavailable."), "anonymous_registration_challenge_unavailable")
        .RateLimited("anonymous_registration_challenge_quota_exceeded");

    [HttpPost("guest-registration-challenges", Name = RouteNames.CreateAnonymousRegistrationChallenge)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.AnonymousRegistrationPolicy)]
    [PrivateNoStore]
    [RequireIdempotencyKey]
    [SuppressIdempotencyResponseStorage]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Create anonymous registration challenge")]
    [EndpointDescription("Issues bounded proof work for the same guest-start body and Idempotency-Key. Creates no order or inventory hold.")]
    [ProducesResponseType(typeof(HalResource<AnonymousRegistrationChallengeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HalResource<AnonymousRegistrationChallengeDto>>> Create(
        Guid eventId,
        [FromBody] StartRegistrationOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // Suppressed response storage deliberately does not claim the intended start key.
        if (Request.Headers["Idempotency-Key"].Count != 1 || idempotencyKey is not { Length: >= 1 and <= 128 }
            || idempotencyKey.Any(character => character is < '!' or > '~'))
        {
            return this.ToValidationProblem(new("idempotencyKey", "Invalid Idempotency-Key", "An intended start key is required."),
                "Idempotency-Key must contain 1 to 128 visible ASCII characters.");
        }

        IdempotencyRequestIdentity identity = await IdempotencyRequestIdentityFactory.CreateIntendedGuestStartAsync(
            HttpContext, eventId, streams, cancellationToken);
        string digest = IdempotencyRequestIdentityFactory.ComputeGuestStartDigest(identity, tenant.TenantId, eventId, idempotencyKey);
        AnonymousRegistrationChallengeIssueResult result = await mediator.Send(
            new IssueAnonymousRegistrationChallengeCommand(eventId, digest, idempotencyKey), cancellationToken);
        if (!result.IsSuccess)
            return Failures.Map(this, result);

        return Ok(new HalResource<AnonymousRegistrationChallengeDto>(result.Challenge!)
            .WithLink(LinkRelations.Submit, HalLink.CreateAction(
                Url.Link(RouteNames.StartGuestRegistrationOrder, new { eventId })!, HttpMethods.Post)));
    }
}
