using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Authorize]
[Route("api/admin/instance/authentication-credential-cutovers")]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class InstanceAuthenticationCredentialCutoversController(
    IQueryHandler<GetCurrentAuthenticationCredentialCutoverQuery, AuthenticationCredentialCutoverDto?> current,
    IQueryHandler<GetAuthenticationCredentialCutoverQuery, AuthenticationCredentialCutoverDto?> operation,
    ICommandHandler<PrepareAuthenticationCredentialCutoverCommand, AuthenticationCredentialCutoverDto> prepare,
    ICommandHandler<ReportAuthenticationCredentialActivationCommand, AuthenticationCredentialCutoverDto> activation,
    ICommandHandler<AttestAuthenticationCredentialFenceCommand, AuthenticationCredentialCutoverDto> fence,
    IQueryHandler<InspectAuthenticationCredentialCleanupQuery, AuthenticationCredentialCleanupInventoryDto> inspection,
    ICommandHandler<CleanupAuthenticationCredentialsCommand, AuthenticationCredentialCutoverDto> cleanup,
    IResourceAssembler<AuthenticationCredentialCutoverDto, AuthenticationCredentialCutoverDto> assembler)
    : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor MissingReceipt = new(
        "Authentication credential cutover not found",
        "The authentication credential cutover was not found.");

    [HttpGet("{operationId:guid}/cleanup-targets", Name = RouteNames.InspectAuthenticationCredentialCleanup)]
    [AllowAnonymous]
    [PrivateNoStore]
    [EndpointSummary("Inspect Exact Authentication Credential Cleanup Targets")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AuthenticationCredentialCleanupInventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthenticationCredentialCleanupInventoryDto>> Inspect(
        Guid operationId, CancellationToken cancellationToken) =>
        Ok(await inspection.QueryAsync(new InspectAuthenticationCredentialCleanupQuery(operationId), cancellationToken));

    [HttpPost("cleanup", Name = RouteNames.CleanupAuthenticationCredentials)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [EndpointSummary("Clean Up Exact Approved Legacy Authentication Credential Rows")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AuthenticationCredentialCutoverDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<AuthenticationCredentialCutoverDto>>> Cleanup(
        AuthenticationCredentialCleanupInputDto input, CancellationToken cancellationToken)
    {
        var dto = await cleanup.ExecuteAsync(new CleanupAuthenticationCredentialsCommand(input), cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpGet("current", Name = RouteNames.GetCurrentAuthenticationCredentialCutover)]
    [AllowAnonymous]
    [PrivateNoStore]
    [EndpointSummary("Get Current Authentication Credential Cutover")]
    [ProducesResponseType(typeof(HalResource<AuthenticationCredentialCutoverDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<AuthenticationCredentialCutoverDto>>> Current(
        CancellationToken cancellationToken)
    {
        var dto = await current.QueryAsync(new GetCurrentAuthenticationCredentialCutoverQuery(), cancellationToken);
        return dto is null ? NoContent() : Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpGet("{operationId:guid}", Name = RouteNames.GetAuthenticationCredentialCutover)]
    [AllowAnonymous]
    [PrivateNoStore]
    [EndpointSummary("Get Authentication Credential Cutover Receipt")]
    [ProducesResponseType(typeof(HalResource<AuthenticationCredentialCutoverDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<AuthenticationCredentialCutoverDto>>> Operation(
        Guid operationId, CancellationToken cancellationToken)
    {
        var dto = await operation.QueryAsync(new GetAuthenticationCredentialCutoverQuery(operationId), cancellationToken);
        return dto is null
            ? this.ToNotFoundProblem(MissingReceipt)
            : Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpPost(Name = RouteNames.PrepareAuthenticationCredentialCutover)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [EndpointSummary("Prepare Authentication Credential Cutover")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AuthenticationCredentialCutoverDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<AuthenticationCredentialCutoverDto>>> Prepare(
        PrepareAuthenticationCredentialCutoverInputDto input, CancellationToken cancellationToken)
    {
        var dto = await prepare.ExecuteAsync(new PrepareAuthenticationCredentialCutoverCommand(input), cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpPost("activations", Name = RouteNames.ReportAuthenticationCredentialActivation)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [EndpointSummary("Report Authentication Credential Acquisition")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AuthenticationCredentialCutoverDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<AuthenticationCredentialCutoverDto>>> Activation(
        AuthenticationCredentialActivationInputDto input, CancellationToken cancellationToken)
    {
        var dto = await activation.ExecuteAsync(new ReportAuthenticationCredentialActivationCommand(input), cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }

    [HttpPost("fence", Name = RouteNames.AttestAuthenticationCredentialFence)]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [EndpointSummary("Attest Authentication Credential Cutover Fences")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AuthenticationCredentialCutoverDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<AuthenticationCredentialCutoverDto>>> Fence(
        AuthenticationCredentialFenceInputDto input, CancellationToken cancellationToken)
    {
        var dto = await fence.ExecuteAsync(new AttestAuthenticationCredentialFenceCommand(input), cancellationToken);
        return Ok(await assembler.ToResource(dto, HttpContext));
    }
}
