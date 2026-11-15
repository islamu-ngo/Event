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
using Explore.Application.Features.Authentication.Continuation;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/auth/account-continuation")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class VerifiedAccountContinuationController(
    IQueryHandler<ResolveVerifiedAccountContinuationQuery, VerifiedAccountContinuationResult> continuation,
    ITenantLookupSource tenantLookup) : ControllerBase
{
    [HttpPost("", Name = RouteNames.ResolveVerifiedAccountContinuation)]
    [TenantReadBootstrap]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Resolve the verified account's tenant destination")]
    [EndpointDescription("Reads an exact validated Local or Keycloak account binding without enrolling an account or membership.")]
    [ProducesResponseType(typeof(VerifiedAccountContinuationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<VerifiedAccountContinuationResult>> Resolve(
        CancellationToken cancellationToken = default)
    {
        // MultiAuth has already validated the principal. Purpose-bound credentials
        // and ambient ATProto identities cannot establish this account evidence.
        var authorization = Request.Headers.Authorization;
        ProviderIdentity? identity = User.GetProviderIdentity();
        if (authorization.Count != 1
            || authorization[0] is not { } bearer
            || !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || identity is null
            || identity.Provider is not ("local" or "keycloak")
            || identity.AccountKey.ProviderKind is not (AuthenticationProviderKind.Local or AuthenticationProviderKind.Keycloak))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Verified account authentication required");
        }

        // An incidental single-tenant default is not an explicit authentication
        // destination. Only resolver inputs backed by the current active directory
        // may constrain the account's own membership navigation.
        bool hasSlug = Request.Headers.TryGetValue(TenantHeaderNames.TenantSlug, out var slugs);
        bool hasHostTenant = HttpContext.Items.TryGetValue(EffectiveHostAuthorityMiddleware.TenantItemKey, out var hostTenant);
        Guid? target = null;
        if (hasSlug || hasHostTenant)
        {
            if (hasSlug && (slugs.Count != 1 || string.IsNullOrWhiteSpace(slugs[0]))
                || !hasSlug && hostTenant is not Guid)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Tenant not resolved");

            try
            {
                var tenants = await tenantLookup.GetTenantLookupsAsync(cancellationToken);
                var matches = tenants.Where(tenant => tenant.TenantId != Guid.Empty
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
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "account_continuation_unavailable");
            }
        }

        VerifiedAccountContinuationResult result = await continuation.QueryAsync(
            new ResolveVerifiedAccountContinuationQuery(identity.AccountKey, target), cancellationToken);
        return result.Outcome == VerifiedAccountContinuationOutcome.Unavailable
            ? Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "account_continuation_unavailable")
            : Ok(result);
    }
}
