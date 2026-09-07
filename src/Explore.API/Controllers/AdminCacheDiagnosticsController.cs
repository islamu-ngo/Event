using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Explore.Application.Authentication;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.Application.Contracts.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/_internal/admin-cache")]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class AdminCacheDiagnosticsController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor AdminCacheDiagnosticsNotFoundProblem = new(
        "Admin cache diagnostics not found",
        "Admin cache diagnostics are not enabled in this environment.");

    [Authorize]
    [HttpPost("current-user/snapshot")]
    public async Task<ActionResult<AdminCacheCurrentUserDiagnostics>> SnapshotCurrentUser(
        [FromServices] IMediator mediator,
        [FromServices] IHostEnvironment hostEnvironment,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(configuration, hostEnvironment))
        {
            return this.ToNotFoundProblem(AdminCacheDiagnosticsNotFoundProblem);
        }

        var providerIdentity = User.GetProviderIdentity();
        var resolvedUserId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);

        return Ok(new AdminCacheCurrentUserDiagnostics(
            User.Identity?.AuthenticationType,
            User.FindFirst(PlatformIdentityClaimTypes.InternalUserId)?.Value,
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            User.FindFirst(JwtRegisteredClaimNames.Sid)?.Value,
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            providerIdentity?.Provider,
            providerIdentity?.ProviderId,
            resolvedUserId));
    }

    [Authorize]
    [HttpPost("users/{userId:guid}/invalidate")]
    public ActionResult InvalidateUser(
        Guid userId,
        [FromServices] IAdminCacheInvalidator adminCacheInvalidator,
        [FromServices] IHostEnvironment hostEnvironment,
        [FromServices] IConfiguration configuration)
    {
        if (!IsEnabled(configuration, hostEnvironment))
        {
            return this.ToNotFoundProblem(AdminCacheDiagnosticsNotFoundProblem);
        }

        adminCacheInvalidator.InvalidateUser(userId);
        return NoContent();
    }

    private static bool IsEnabled(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        return configuration.GetValue<bool>("Diagnostics:EnableAdminCacheInvalidation") &&
            (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Testing"));
    }

    public sealed record AdminCacheCurrentUserDiagnostics(
        string? AuthenticationType,
        string? InternalUserIdClaim,
        string? SubjectClaim,
        string? SessionIdClaim,
        string? NameIdentifierClaim,
        string? Provider,
        string? ProviderId,
        Guid? ResolvedUserId);
}
