using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Middleware;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Continuation;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/auth/tenant-join")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class VerifiedTenantJoinController(
    ICommandHandler<JoinVerifiedTenantCommand, BaseCommandResponse<Guid>> joining,
    ITenantLookupSource tenantLookup) : ControllerBase
{
    [HttpPost("", Name = RouteNames.JoinVerifiedTenant)]
    [TenantReadBootstrap]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Join the explicitly selected tenant with an existing verified account")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Join(CancellationToken cancellationToken = default)
    {
        var authorization = Request.Headers.Authorization;
        var identity = User.GetProviderIdentity();
        if (authorization.Count != 1 || authorization[0] is not { } bearer
            || !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || identity is null || identity.Provider is not ("local" or "keycloak")
            || identity.AccountKey.ProviderKind is not (AuthenticationProviderKind.Local or AuthenticationProviderKind.Keycloak))
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Verified account authentication required");

        bool hasSlug = Request.Headers.TryGetValue(TenantHeaderNames.TenantSlug, out var slugs);
        bool hasHost = HttpContext.Items.TryGetValue(EffectiveHostAuthorityMiddleware.TenantItemKey, out var hostTenant);
        if (!hasSlug && !hasHost)
            return Conflict(BaseCommandResponse.Failure<Guid>("tenant_join_target_required"));
        if (hasSlug && (slugs.Count != 1 || string.IsNullOrWhiteSpace(slugs[0]))
            || !hasSlug && hostTenant is not Guid)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Tenant not resolved");

        Guid target;
        try
        {
            var directory = await tenantLookup.GetTenantLookupsAsync(cancellationToken);
            var matches = directory.Where(tenant => tenant.TenantId != Guid.Empty
                    && (hasSlug
                        ? string.Equals(tenant.Slug, slugs[0]!.Trim(), StringComparison.OrdinalIgnoreCase)
                        : tenant.TenantId == (Guid)hostTenant!))
                .Take(2).ToArray();
            if (matches.Length != 1)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Tenant not resolved");
            target = matches[0].TenantId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "tenant_join_unavailable");
        }

        try
        {
            // Native CQS selects this exact Runtime owner before constructing the handler.
            var result = await joining.ExecuteAsync(
                new JoinVerifiedTenantCommand(identity.AccountKey, target), cancellationToken);
            return result.IsSuccess ? Ok(result) : Conflict(result);
        }
        catch (HumanMutationOutcomeUnknownException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "tenant_join_outcome_unknown");
        }
        catch (UnauthorizedAccessException)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Tenant enrollment authority changed");
        }
    }
}
