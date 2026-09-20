using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.StatusType;
using Explore.Application.Features.StatusTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class ApprovalStatusController(
    IQueryHandler<GetStatusTypeListRequest, List<StatusTypeListDto>> approvalStatuses) : ControllerBase
{

    [HttpGet(Name = RouteNames.GetApprovalStatusOptions)]
    [EndpointSummary("Get all Status Types")]
    [EndpointDescription("Get A List of all the Status Type Options")]
    [AllowAnonymous]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<StatusTypeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var statusTypes = await approvalStatuses.QueryAsync(new GetStatusTypeListRequest(), cancellationToken);
        return Ok(statusTypes);
    }
}
