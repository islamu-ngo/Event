using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/_internal/auth-probe")]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class AuthContextProbeController : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor AuthProbeNotFoundProblem = new(
        "Authentication context probe not found",
        "Authentication context probe is not enabled in this environment.");

    [Authorize]
    [HttpGet("secure")]
    public ActionResult<AuthContextProbeResponse> GetSecure([FromServices] ITenantContextAccessor tenantContextAccessor, [FromServices] IHostEnvironment hostEnvironment, [FromServices] IConfiguration configuration)
    {
        if (!hostEnvironment.IsEnvironment("Testing") || !configuration.GetValue<bool>("Diagnostics:EnableAuthContextProbe"))
        {
            return this.ToNotFoundProblem(AuthProbeNotFoundProblem);
        }

        var apiKeyContext = User.TryGetApiKeyPrincipalContext();

        return Ok(new AuthContextProbeResponse
        {
            AuthenticationType = User.Identity?.AuthenticationType,
            AuthMethod = User.GetAuthenticationMethod(),
            ApiKeyId = apiKeyContext?.KeyId,
            OwnerType = apiKeyContext?.OwnerType.ToString(),
            OwnerId = apiKeyContext?.OwnerId.ToString(),
            TenantId = tenantContextAccessor.TenantId,
            UserId = User.GetAuthenticatedUserId()
        });
    }

    public sealed record AuthContextProbeResponse
    {
        public string? AuthenticationType { get; init; }

        public string? AuthMethod { get; init; }

        public string? ApiKeyId { get; init; }

        public string? OwnerType { get; init; }

        public string? OwnerId { get; init; }

        public Guid? TenantId { get; init; }

        public Guid? UserId { get; init; }
    }
}
