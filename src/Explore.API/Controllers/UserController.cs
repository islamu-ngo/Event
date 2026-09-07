using System.Security.Claims;
using Explore.Application.Authentication;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
public class UserController : EventControllerBase
{
    private readonly IMediator _mediator;
    private readonly IResourceAssembler<UserDto, UserDto> _resourceAssembler;

    private static readonly ApiValidationProblemDescriptor SyncValidationProblem = new(
        "user",
        "User synchronization validation failed",
        "User synchronization failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "user",
        "User validation failed",
        "User update failed.");

    private static readonly ApiNotFoundProblemDescriptor UserNotFoundProblem = new(
        "User not found",
        "The requested user could not be found.");

    public UserController(IMediator mediator, IResourceAssembler<UserDto, UserDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Syncs the authenticated user from the active identity provider to the local database.
    /// Creates a new User and Actor if they don't exist, otherwise updates the user's basic info.
    /// Call this endpoint after login/registration to ensure user exists in the system.
    /// </summary>
    [HttpPost("sync", Name = RouteNames.SyncUser)]
    [Authorize]
    [EndpointSummary("Sync user from identity provider")]
    [EndpointDescription("Creates or updates the user in the local database and ensures external provider linkage. Also creates the user's personal Actor if new user. Call this after login/registration.")]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SyncUser(CancellationToken cancellationToken = default)
    {
        var providerIdentity = User.GetProviderIdentity();
        if (providerIdentity is null)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "Could not resolve provider identity from authentication token.");
        }

        var email = providerIdentity.Email;
        var firstName = User.GetFirstName() ?? string.Empty;
        var lastName = User.GetLastName() ?? string.Empty;
        var provider = providerIdentity.Provider;
        var providerId = providerIdentity.ProviderId;
        var emailVerified = providerIdentity.EmailVerified;

        var userDto = new UserDto
        {
            Id = Guid.Empty,
            Email = email,
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "User" : firstName,
            LastName = string.IsNullOrWhiteSpace(lastName) ? "" : lastName,
            AuthProvider = provider,
            AuthProviderId = providerId,
            EmailVerified = emailVerified
        };

        var command = new SyncUserCommand
        {
            AccountKey = providerIdentity.AccountKey,
            UserDto = userDto
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, SyncValidationProblem);
        }

        return Ok(response);
    }

    [HttpGet(Name = RouteNames.GetCurrentUser)]
    [Authorize]
    [ProducesResponseType(typeof(HalResource<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<UserDto>>> GetCurrentUser(CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var query = new GetUserRequest { UserId = currentUserId.Value };
        var user = await _mediator.Send(query, cancellationToken);
        if (user is null)
        {
            return this.ToNotFoundProblem(UserNotFoundProblem);
        }

        return Ok(await _resourceAssembler.ToResource(user, HttpContext));
    }

    /// <summary>
    /// Returns the persisted admin authority of the current authenticated user.
    /// Used by the BFF claims transformation to enrich the ClaimsPrincipal with admin claims.
    /// </summary>
    [HttpGet("admin-authority", Name = RouteNames.GetCurrentUserAdminAuthority)]
    [Authorize]
    [EndpointSummary("Get current user's admin authority")]
    [EndpointDescription("Returns instance, tenant, organization, and group admin status for the authenticated user. Consumed by BFF and route authorization.")]
    public async Task<ActionResult<AdminAuthorityDto>> GetAdminAuthority(CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var query = new GetAdminAuthorityRequest { UserId = currentUserId.Value };
        var authority = await _mediator.Send(query, cancellationToken);

        return Ok(authority);
    }

    /// <summary>
    /// Resolves the appropriate tenant redirection target for the authenticated user.
    /// </summary>
    [HttpGet("tenants/redirection", Name = RouteNames.ResolveUserTenantRedirection)]
    [Authorize]
    [EndpointSummary("Resolve user tenant redirection")]
    [EndpointDescription("Resolves the tenant (slug and ID) to redirect the user to from the root platform domain based on active memberships and active history.")]
    [ProducesResponseType(typeof(UserTenantRedirectionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserTenantRedirectionDto>> ResolveTenantRedirection(CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var query = new ResolveUserTenantRedirectionRequest { UserId = currentUserId.Value };
        var redirection = await _mediator.Send(query, cancellationToken);

        return Ok(redirection);
    }

    /// <summary>
    /// Updates the authenticated user's last active tenant setting.
    /// </summary>
    [HttpPost("active-tenant/{tenantId:guid}", Name = RouteNames.UpdateUserLastActiveTenant)]
    [Authorize]
    [EndpointSummary("Update user's last active tenant")]
    [EndpointDescription("Updates the user's last active tenant tracking value in the database, verifying membership first.")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> UpdateLastActiveTenant(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new UpdateUserLastActiveTenantCommand
        {
            UserId = currentUserId.Value,
            TenantId = tenantId
        };
        var success = await _mediator.Send(command, cancellationToken);

        return Ok(success);
    }

    /// <summary>
    /// Gets all organizations the specified user is a member of.
    /// Returns the user's role in each organization.
    /// </summary>
    [HttpGet("{userId:guid}/organizations", Name = RouteNames.GetUserOrganizations)]
    [Authorize]
    [EndpointSummary("Get user's organizations")]
    [EndpointDescription("Gets all organizations the user is a member of, including their role in each organization.")]
    [ProducesResponseType(typeof(List<OrganizationListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<OrganizationListDto>>> GetUserOrganizations(Guid userId, CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var query = new GetUserOrganizationsRequest { UserId = userId };
        var organizations = await _mediator.Send(query, cancellationToken);

        return Ok(organizations);
    }

    [HttpPatch("{id:guid}", Name = RouteNames.UpdateCurrentUser)]
    [Authorize]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateUser(
        Guid id,
        [FromBody] UpdateUserDto userDto,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        if (id != currentUserId.Value)
        {
            return this.ToForbiddenProblem(detail: "You can only update your own profile.");
        }

        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current user concurrency stamp.");
        }

        var command = new UpdateUserCommand
        {
            UserId = id,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            UpdateUserDto = userDto
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(UserNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [HttpDelete(Name = RouteNames.DeleteCurrentUser)]
    [Authorize]
    [ProducesResponseType(typeof(PrivacyErasureStartDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PrivacyErasureStartDto>> DeleteUser(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!currentUserId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem();
        }

        if (!Guid.TryParse(idempotencyKey, out Guid intentId)
            || intentId.Version != 7
            || intentId.Variant is < 8 or > 11)
        {
            ModelState.AddModelError("Idempotency-Key", "A UUIDv7 Idempotency-Key header is required.");
            return ValidationProblem(ModelState);
        }

        var command = new DeleteUserCommand { UserId = currentUserId.Value, IntentId = intentId };
        PrivacyErasureStartDto result = await _mediator.Send(command, cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.RetryAfter = "5";
        return AcceptedAtRoute(RouteNames.GetPrivacyErasureStatus, routeValues: null, value: result);
    }

}
