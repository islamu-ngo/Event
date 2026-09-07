namespace Explore.API.Attributes;

/// <summary>
/// Classification of an API endpoint by intended audience and protection level.
/// Every controller action in Explore.API MUST be tagged with exactly one value
/// via <see cref="EndpointClassificationAttribute"/> (controller-level or action-level).
/// Enforced by <c>EndpointClassificationArchitectureTests</c> and surfaced into
/// the OpenAPI document as the <c>x-endpoint-class</c> operation extension.
/// See <c>docs/GOVERNANCE.md#api-contract-rules</c> for policy details.
/// </summary>
public enum EndpointClass
{
    /// <summary>
    /// Anonymous / no authentication required. Typically paired with <c>[AllowAnonymous]</c>.
    /// Protected only by the global IP-based rate limit. Safe to expose to any network.
    /// </summary>
    Public = 0,

    /// <summary>
    /// Any authenticated user. Typically paired with <c>[Authorize]</c> without a role argument.
    /// Protected by the authenticated or write rate-limit policy. Default for logged-in user actions.
    /// </summary>
    Authenticated = 1,

    /// <summary>
    /// Restricted to administrators or setup-secret holders. Typically paired with
    /// <c>[Authorize(Policy = ApiAuthorizationPolicies.Admin)]</c> / <c>[Authorize(Roles = "InstanceAdmin")]</c>,
    /// or gated by the setup-secret flow. Used for tenant / instance administration.
    /// </summary>
    Admin = 2,

    /// <summary>
    /// Anonymous transactional write endpoint. Requires the public transactional
    /// rate-limit policy and an Idempotency-Key for POST requests.
    /// </summary>
    PublicTransactional = 3
}
