using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Authentication;
using Explore.Application.DTOs.Notification;
using Explore.Application.DTOs.Organization;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Features.Organizations.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// Organization management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class OrganizationController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "organization",
        "Organization validation failed",
        "Organization creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "organization",
        "Organization validation failed",
        "Organization update failed.");

    private static readonly ApiValidationProblemDescriptor PreferenceValidationProblem = new(
        "organizationNotificationPreferences",
        "Organization notification preference validation failed",
        "Organization notification preference update failed.");

    private static readonly ApiNotFoundProblemDescriptor DeleteNotFoundProblem = new(
        "Organization not found",
        "Organization not found.");

    private static readonly ApiNotFoundProblemDescriptor OrganizationNotFoundProblem = new(
        "Organization not found",
        "Organization not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<OrganizationDto, OrganizationListDto> _resourceAssembler;
    private readonly IResourceAssembler<NotificationPreferenceMatrixDto> _preferenceAssembler;

    public OrganizationController(
        IMediator mediator,
        IResourceAssembler<OrganizationDto, OrganizationListDto> resourceAssembler,
        IResourceAssembler<NotificationPreferenceMatrixDto> preferenceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
        _preferenceAssembler = preferenceAssembler;
    }

    /// <summary>
    /// Get all organizations with pagination.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetOrganizations)]
    [EndpointSummary("Get all Organizations")]
    [EndpointDescription("Get a paginated list of all Organizations. Default page size is 20, max is 100. " +
        "Response includes HATEOAS navigation links (first, prev, next, last). " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<OrganizationListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<OrganizationListDto>>> GetAll(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetOrganizationListRequest
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetOrganizations,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get organizations where the current user is a member.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("my", Name = RouteNames.GetMyOrganizations)]
    [EndpointSummary("Get my Organizations")]
    [EndpointDescription("Get a paginated list of organizations where the current user is a member. " +
        "Default page size is 20, max is 100.")]
    [ProducesResponseType(typeof(HalCollectionResource<OrganizationListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HalCollectionResource<OrganizationListDto>>> GetMyOrganizations(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        }

        var result = await _mediator.Send(new GetMyOrganizationsRequest
        {
            UserId = userId.Value.ToString("D"),
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetMyOrganizations,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get organization details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetOrganizationById)]
    [EndpointSummary("Get Organization Details")]
    [EndpointDescription("Get full details of an organization including actor information and approval status. " +
        "Response includes links to related resources (events, members).")]
    [ProducesResponseType(typeof(HalResource<OrganizationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<OrganizationDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await _mediator.Send(new GetOrganizationDetailsRequest { Id = id }, cancellationToken);
        if (organization == null)
        {
            return this.ToNotFoundProblem(OrganizationNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(organization, HttpContext);
        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}/notification-preferences", Name = RouteNames.GetOrganizationNotificationPreferences)]
    [EndpointSummary("Get Organization Notification Preferences")]
    [EndpointDescription("Get the effective notification preference matrix for an organization scope.")]
    [ProducesResponseType(typeof(HalResource<NotificationPreferenceMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<NotificationPreferenceMatrixDto>>> GetNotificationPreferences(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var matrix = await _mediator.Send(new GetOrganizationNotificationPreferenceMatrixQuery
        {
            OrganizationId = id
        }, cancellationToken);

        var halResource = await _preferenceAssembler.ToResource(matrix, HttpContext);
        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}/notification-preferences", Name = RouteNames.UpdateOrganizationNotificationPreferences)]
    [EndpointSummary("Update Organization Notification Preferences")]
    [EndpointDescription("Patch supplied organization-scoped notification preference cells while preserving omitted cells.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateNotificationPreferences(
        Guid id,
        [FromBody] UpdateNotificationPreferenceMatrixDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UpdateOrganizationNotificationPreferenceMatrixCommand
        {
            OrganizationId = id,
            Cells = request.Cells
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, PreferenceValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("{id:guid}/notification-preferences/mute", Name = RouteNames.SetOrganizationNotificationPreferenceMute)]
    [EndpointSummary("Set Organization Notification Preference Mute")]
    [EndpointDescription("Set organization-scoped global mute for non-essential notification preferences.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetNotificationPreferenceMute(
        Guid id,
        [FromBody] SetNotificationPreferenceMuteDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new SetOrganizationNotificationPreferenceMuteCommand
        {
            OrganizationId = id,
            IsMuted = request.IsMuted
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, PreferenceValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Create a new organization.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateOrganization)]
    [EndpointSummary("Create Organization")]
    [EndpointDescription("Create a new organization. The authenticated user becomes the owner.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateOrganizationDto organization, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        }

        var command = new CreateOrganizationCommand
        {
            OrganizationDto = organization,
            CreatorUserId = userId.Value
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        // Return 201 Created with Location header pointing to the new resource
        return CreatedAtRoute(
            RouteNames.GetOrganizationById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Partially update an organization's editable profile fields.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateOrganization)]
    [EndpointSummary("Update Organization")]
    [EndpointDescription("Partially update an existing organization. Route ID is authoritative and If-Match must contain the current organization concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateOrganizationDto updateDto,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        }

        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current organization concurrency stamp.");
        }

        var command = new UpdateOrganizationCommand
        {
            OrganizationId = id,
            UserId = userId,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            UpdateOrganizationDto = updateDto
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.FailureCode == FailureCodes.NotFound
                    ? this.ToNotFoundProblem(OrganizationNotFoundProblem)
                    : this.ToCommandValidationProblem(result, UpdateValidationProblem);
        }

        return Ok(result);
    }

    /// <summary>
    /// Update organization approval status (Admin only).
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("{id:guid}/approval-status", Name = RouteNames.UpdateOrganizationApprovalStatus)]
    [EndpointSummary("Update Organization Approval Status")]
    [EndpointDescription("Update the approval status of an organization. Requires Admin role.")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateApprovalStatus(
        Guid id,
        [FromBody] UpdateOrganizationApprovalStatusDto approvalStatus, CancellationToken cancellationToken = default)
    {
        var command = new UpdateOrganizationApprovalStatusCommand
        {
            OrganizationId = id,
            ApprovalStatusDto = approvalStatus
        };

        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Delete an organization.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteOrganization)]
    [EndpointSummary("Delete Organization")]
    [EndpointDescription("Delete an organization. Requires ownership or Admin role.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");
        }

        var result = await _mediator.Send(new DeleteOrganizationCommand
        {
            Id = id,
            UserId = userId
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToNotFoundProblem(DeleteNotFoundProblem);
        }

        return NoContent();
    }

}
