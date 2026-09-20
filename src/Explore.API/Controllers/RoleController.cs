using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Role;
using Explore.Application.Features.Roles.Requests.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class RoleController(
    IQueryHandler<GetRoleListRequest, List<RoleListDto>> getRoleListHandler,
    IQueryHandler<GetRoleDetailsRequest, RoleDto?> getRoleDetailsHandler) : ControllerBase
{

    // GET: api/role?roleScopeId=2
    [HttpGet(Name = RouteNames.GetRoles)]
    [EndpointSummary("Get all Roles")]
    [EndpointDescription("Retrieve roles, optionally filtered by normalized roleScopeId. Returns all roles when no roleScopeId is specified.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<RoleListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<RoleListDto>>> GetAll(
        [FromQuery] int? roleScopeId = null,
        CancellationToken cancellationToken = default)
    {
        var roles = await getRoleListHandler.QueryAsync(new GetRoleListRequest { RoleScopeId = roleScopeId }, cancellationToken);
        return Ok(roles);
    }

    // GET: api/role/{id}
    [HttpGet("{id}", Name = RouteNames.GetRoleById)]
    [EndpointSummary("Get Role by ID")]
    [EndpointDescription("Retrieve details of a specific role including scope and system flag.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<RoleDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var role = await getRoleDetailsHandler.QueryAsync(new GetRoleDetailsRequest { Id = id }, cancellationToken);

        return Ok(role);
    }
}
