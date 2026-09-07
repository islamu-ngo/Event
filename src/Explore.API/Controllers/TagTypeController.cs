using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypes.Requests.Queries;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class TagTypeController(IMediator mediator) : ControllerBase
{

    // GET: api/tagtype
    [HttpGet(Name = RouteNames.GetTagTypes)]
    [AllowAnonymous]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<TagTypeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var tagTypes = await mediator.Send(new GetTagTypeListRequest(), cancellationToken);
        return Ok(tagTypes);
    }

    // GET: api/tagtype/{id}
    [HttpGet("{id}", Name = RouteNames.GetTagTypeById)]
    [AllowAnonymous]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<TagTypeDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var tagType = await mediator.Send(new GetTagTypeDetailsRequest { Id = id }, cancellationToken);
        return Ok(tagType);
    }

    // GET: api/tagtype/with-tags
    [HttpGet("with-tags", Name = RouteNames.GetTagTypesWithTags)]
    [EndpointSummary("Get Tag Types with Tags")]
    [EndpointDescription("Returns all tag types with their associated tags grouped. Used by the tri-state tag filter dropdown.")]
    [AllowAnonymous]
    [OutputCache(PolicyName = "LookupData")]
    [ProducesResponseType(typeof(List<TagTypeWithTagsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TagTypeWithTagsDto>>> GetWithTags(CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetTagsGroupedByTagTypeRequest(), cancellationToken);
        return Ok(result);
    }
}
