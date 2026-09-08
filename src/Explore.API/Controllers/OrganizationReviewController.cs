using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Features.OrganizationReviews.Commands.CreateOrganizationReview;
using Explore.Application.Features.OrganizationReviews.Queries.GetMyReviews;
using Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class OrganizationReviewController : EventControllerBase
{
    private readonly IMediator _mediator;
    private readonly IResourceAssembler<OrganizationReviewDto, OrganizationReviewDto> _resourceAssembler;

    public OrganizationReviewController(IMediator mediator, IResourceAssembler<OrganizationReviewDto, OrganizationReviewDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetOrganizationReviews)]
    [ProducesResponseType(typeof(HalCollectionResource<OrganizationReviewDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<OrganizationReviewDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var reviews = await _mediator.Send(new GetOrganizationReviewsQuery(), cancellationToken);
        var halResource = await _resourceAssembler.ToCollectionResource(
            reviews,
            RouteNames.GetOrganizationReviews,
            HttpContext);
        return Ok(halResource);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{organizationId:guid}", Name = RouteNames.GetOrganizationReviewsByOrganization)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<List<OrganizationReviewDto>>> Get(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var reviews = await _mediator.Send(new GetOrganizationReviewsQuery { OrganizationId = organizationId }, cancellationToken);
        return Ok(reviews);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("user/{userId:guid}", Name = RouteNames.GetOrganizationReviewsByUser)]
    public async Task<ActionResult<List<OrganizationReviewDto>>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        var reviews = await _mediator.Send(new GetMyReviewsQuery { UserId = userId }, cancellationToken);
        return Ok(reviews);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateOrganizationReview)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Post([FromBody] CreateOrganizationReviewDto createOrganizationReviewDto, CancellationToken cancellationToken = default)
    {
        var command = new CreateOrganizationReviewCommand
        {
            CreateOrganizationReviewDto = createOrganizationReviewDto,
            ReviewerUserId = RequiredUserId
        };
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }
}
