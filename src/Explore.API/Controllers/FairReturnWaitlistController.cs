using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Waitlist;
using Explore.Application.Features.Waitlist.Requests.Commands;
using Explore.Application.Features.Waitlist.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route(
    "api/events/{eventId:guid}/" +
    "registration-orders/" +
    "{registrationOrderId:guid}/lines/" +
    "{registrationOrderLineId:guid}/waitlist")]
[ApiController]
public sealed class FairReturnWaitlistController(
    IMediator mediator,
    IResourceAssembler<
        FairReturnWaitlistDto,
        FairReturnWaitlistDto> assembler) :
    ControllerBase
{
    private const string CapabilityHeader =
        "X-Registration-Order-Capability";
    private static readonly
        ApiNotFoundProblemDescriptor Unavailable =
        new(
            "Waitlist unavailable",
            "Waitlist state was not found or is unavailable.",
            "waitlist_unavailable");

    [HttpGet("", Name = RouteNames.GetFairReturnWaitlist)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [ProducesResponseType(
        typeof(HalResource<FairReturnWaitlistDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<FairReturnWaitlistDto>>> Get(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken) =>
        await ResourceAsync(
            await mediator.Send(
                new GetFairReturnWaitlistQuery(
                    eventId,
                    registrationOrderId,
                    registrationOrderLineId,
                    capabilityToken),
                cancellationToken));

    [HttpPost("", Name = RouteNames.JoinFairReturnWaitlist)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(HalResource<FairReturnWaitlistDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<FairReturnWaitlistDto>>> Join(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken) =>
        await ResourceAsync(
            await mediator.Send(
                new JoinFairReturnWaitlistCommand(
                    eventId,
                    registrationOrderId,
                    registrationOrderLineId),
                cancellationToken));

    [HttpDelete("", Name = RouteNames.LeaveFairReturnWaitlist)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(HalResource<FairReturnWaitlistDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<FairReturnWaitlistDto>>> Leave(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken) =>
        await ResourceAsync(
            await mediator.Send(
                new LeaveFairReturnWaitlistCommand(
                    eventId,
                    registrationOrderId,
                    registrationOrderLineId),
                cancellationToken));

    [HttpPost(
        "offers/{offerId:guid}/accept",
        Name = RouteNames.AcceptFairReturnOffer)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(HalResource<FairReturnWaitlistDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<FairReturnWaitlistDto>>>
        AcceptOffer(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid offerId,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken) =>
        await ResourceAsync(
            await mediator.Send(
                new AcceptFairReturnOfferCommand(
                    eventId,
                    registrationOrderId,
                    registrationOrderLineId,
                    offerId),
                cancellationToken));

    [HttpDelete(
        "supply/{supplyId:guid}",
        Name = RouteNames.WithdrawFairReturnSupply)]
    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay]
    [EndpointClassification(
        EndpointClass.Authenticated)]
    [ProducesResponseType(
        typeof(HalResource<FairReturnWaitlistDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<FairReturnWaitlistDto>>>
        WithdrawSupply(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid supplyId,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken) =>
        await ResourceAsync(
            await mediator.Send(
                new WithdrawFairReturnSupplyCommand(
                    eventId,
                    registrationOrderId,
                    registrationOrderLineId,
                    supplyId),
                cancellationToken));

    private async Task<ActionResult<
        HalResource<FairReturnWaitlistDto>>>
        ResourceAsync(
            FairReturnWaitlistDto? dto) =>
        dto is null
            ? this.ToNotFoundProblem(Unavailable)
            : Ok(await assembler.ToResource(
                dto,
                HttpContext));
}
