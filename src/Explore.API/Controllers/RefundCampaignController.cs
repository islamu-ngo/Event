using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/refund-campaigns")]
[ApiController]
public sealed class RefundCampaignController(IMediator mediator) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Refund campaign not found", "Refund campaign was not found.");

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet(Name = RouteNames.GetRefundCampaigns)]
    [ProducesResponseType(typeof(HalCollectionResource<RefundCampaignDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<RefundCampaignDto>>> GetList(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RefundCampaignDto> campaigns = await mediator.Send(
            new GetRefundCampaignsQuery(eventId), cancellationToken);
        HalResource<RefundCampaignDto>[] resources = campaigns.Select(campaign => ToResource(campaign, eventId)).ToArray();
        return Ok(HalCollectionResource<RefundCampaignDto>.Create(
            resources,
            pageNumber: 1,
            pageSize: Math.Max(1, resources.Length),
            totalCount: resources.Length,
            new Dictionary<string, HalLink>
            {
                [LinkRelations.Self] = HalLink.Create(Url.Link(RouteNames.GetRefundCampaigns, new { eventId })!)
            }));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{campaignId:guid}", Name = RouteNames.GetRefundCampaign)]
    [ProducesResponseType(typeof(HalResource<RefundCampaignDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RefundCampaignDto>>> Get(
        Guid eventId,
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        RefundCampaignDto? campaign = await mediator.Send(
            new GetRefundCampaignQuery(eventId, campaignId), cancellationToken);
        return campaign is null ? this.ToNotFoundProblem(NotFoundProblem) : Ok(ToResource(campaign, eventId));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control", "Location")]
    [PrivateNoStore]
    [HttpPost("{campaignId:guid}/resume", Name = RouteNames.ResumeRefundCampaign)]
    [ProducesResponseType(typeof(HalResource<RefundCampaignDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RefundCampaignDto>>> Resume(
        Guid eventId,
        Guid campaignId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _ = idempotencyKey;
        RefundCampaignDto? campaign = await mediator.Send(
            new ResumeRefundCampaignCommand(eventId, campaignId), cancellationToken);
        return campaign is null
            ? this.ToNotFoundProblem(NotFoundProblem)
            : Accepted(ToResource(campaign, eventId));
    }

    private HalResource<RefundCampaignDto> ToResource(RefundCampaignDto campaign, Guid eventId)
    {
        var values = new { eventId, campaignId = campaign.Id };
        var resource = new HalResource<RefundCampaignDto>(campaign)
            .WithLink(LinkRelations.Self, HalLink.Create(Url.Link(RouteNames.GetRefundCampaign, values)!));
        if (campaign.StatusCode is "Pending" or "RequiresOperator")
        {
            resource.WithLink(LinkRelations.ResumeRefundCampaign, HalLink.CreateAction(
                Url.Link(RouteNames.ResumeRefundCampaign, values)!, HttpMethods.Post));
        }
        return resource;
    }
}
