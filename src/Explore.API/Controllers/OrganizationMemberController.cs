using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Authorize]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class OrganizationMemberController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor OrganizationMemberNotFoundProblem = new(
        "Organization member not found",
        "Organization member not found.");

    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<OrganizationMemberDto, OrganizationMemberDto> _resourceAssembler;

    public OrganizationMemberController(
        IMediator mediator,
        ITenantContext tenantContext,
        IResourceAssembler<OrganizationMemberDto, OrganizationMemberDto> resourceAssembler)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
        _resourceAssembler = resourceAssembler;
    }

    [HttpGet("{organizationId:guid}", Name = RouteNames.GetOrganizationMembersByOrganization)]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType(typeof(HalCollectionResource<OrganizationMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<OrganizationMemberDto>>> Get(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var members = await _mediator.Send(
            new GetOrganizationMembersRequest
            {
                OrganizationId = organizationId,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);
        var halResource = await _resourceAssembler.ToCollectionResource(
            members,
            RouteNames.GetOrganizationMembersByOrganization,
            new { organizationId, tenantId = _tenantContext.TenantId },
            HttpContext);

        return Ok(halResource);
    }

    [HttpGet("member/{id:guid}", Name = RouteNames.GetOrganizationMemberById)]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType(typeof(HalResource<OrganizationMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<OrganizationMemberDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await _mediator.Send(
            new GetOrganizationMemberDetailsRequest
            {
                Id = id,
                TenantId = _tenantContext.TenantId
            },
            cancellationToken);
        if (member is null)
        {
            return this.ToNotFoundProblem(OrganizationMemberNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(member, HttpContext);
        return Ok(halResource);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.AddOrganizationMember)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Post([FromBody] AddOrganizationMemberDto dto, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new AddOrganizationMemberCommand
        {
            AddOrganizationMemberDto = dto,
            RequesterUserId = userId,
            TenantId = _tenantContext.TenantId
        };
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("role", Name = RouteNames.UpdateOrganizationMemberRole)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateRole([FromBody] UpdateOrganizationMemberRoleDto dto, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new UpdateOrganizationMemberRoleCommand { UpdateOrganizationMemberRoleDto = dto, RequesterUserId = userId };
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("invitations", Name = RouteNames.GetMyOrganizationInvitations)]
    public async Task<ActionResult<List<OrganizationInvitationDto>>> GetMyInvitations(CancellationToken cancellationToken = default)
    {
        var email = User.GetEmail();
        if (string.IsNullOrEmpty(email))
        {
            return this.ToAuthenticationRequiredProblem(
                title: "Email claim not found",
                detail: "The authenticated principal does not include an email claim required to list invitations.");
        }

        var response = await _mediator.Send(new GetMyInvitationsRequest { Email = email }, cancellationToken);
        return Ok(response);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("invitations/{id:guid}/accept", Name = RouteNames.AcceptOrganizationInvitation)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> AcceptInvitation(Guid id, CancellationToken cancellationToken = default)
    {
        var userGuid = CurrentUserId;
        if (!userGuid.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new AcceptInvitationCommand { InvitationId = id, UserId = userGuid.Value };
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("invitations/{id:guid}/decline", Name = RouteNames.DeclineOrganizationInvitation)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DeclineInvitation(Guid id, CancellationToken cancellationToken = default)
    {
        var userGuid = CurrentUserId;
        if (!userGuid.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }
        var command = new DeclineInvitationCommand { InvitationId = id, UserId = userGuid.Value };
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteOrganizationMember)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new DeleteOrganizationMemberCommand { MemberId = id, RequesterUserId = userId };
        var response = await _mediator.Send(command, cancellationToken);
        return Ok(response);
    }

}
