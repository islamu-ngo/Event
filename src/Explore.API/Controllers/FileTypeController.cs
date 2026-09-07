using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.FileType;
using Explore.Application.Features.FileTypes.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class FileTypeController(IMediator mediator) : ControllerBase
{

    // GET: api/filetype
    [HttpGet(Name = RouteNames.GetFileTypes)]
    [EndpointSummary("Get all File Types")]
    [EndpointDescription("Retrieve a list of all file types (Image, Document, Video, Audio)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<FileTypeListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<FileTypeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var fileTypes = await mediator.Send(new GetFileTypeListRequest(), cancellationToken);
        return Ok(fileTypes);
    }

    // GET: api/filetype/{id}
    [HttpGet("{id}", Name = RouteNames.GetFileTypeById)]
    [EndpointSummary("Get File Type by ID")]
    [EndpointDescription("Retrieve details of a specific file type")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<FileTypeDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var fileType = await mediator.Send(new GetFileTypeDetailsRequest { Id = id }, cancellationToken);
        return Ok(fileType);
    }
}
