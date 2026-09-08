using Explore.Application.Authentication;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Analytics;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Instance-wide object storage configuration, connectivity checks, and usage recalculation.
/// </summary>
/// <remarks>
/// Split out of InstanceSettingsController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/instance/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class InstanceStorageSettingsController : InstanceSettingsControllerBase
{
    private readonly IMediator _mediator;
    private readonly IResourceAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsDto> _storageSettingsAssembler;

    public InstanceStorageSettingsController(
        IMediator mediator,
        IResourceAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsDto> storageSettingsAssembler,
        IAdminContext adminContext,
        ISetupSecretProvider setupSecretProvider)
        : base(adminContext, setupSecretProvider)
    {
        _mediator = mediator;
        _storageSettingsAssembler = storageSettingsAssembler;
    }

    [HttpGet("storage", Name = RouteNames.GetInstanceStorageSettings)]
    [EndpointSummary("Get Instance Storage Settings")]
    [EndpointDescription("Returns provider policy, quotas, usage, health, and redacted optional S3 settings. Only instance admins can access.")]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<InstanceStorageSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceStorageSettingsDto>>> GetStorageSettings(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");
        var settings = await _mediator.Send(new GetInstanceStorageSettingsQuery(), cancellationToken);
        var halResource = await _storageSettingsAssembler.ToResource(settings, HttpContext);
        return Ok(halResource);
    }

    [HttpPatch("storage", Name = RouteNames.UpdateInstanceStorageSettings)]
    [EndpointSummary("Update Instance Storage Settings")]
    [EndpointDescription("Updates instance storage provider policy, quotas, delegation lock, and optional S3 settings. Requires instance administrator.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateStorageSettings(
        [FromBody] PatchInstanceStorageSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue) return this.ToAuthenticationRequiredProblem(detail: "The authenticated principal could not be resolved to an application user.");

        var response = await _mediator.Send(new UpdateInstanceStorageSettingsCommand { UserId = userId.Value, Patch = settings }, cancellationToken);
        return HandleCommandResponse(response);
    }

    [HttpPost("storage/test", Name = RouteNames.TestInstanceStorageConnection)]
    [EndpointSummary("Test Storage Connection")]
    [EndpointDescription("Tests the currently selected storage provider using current instance settings.")]
    [ProducesResponseType(typeof(InstanceStorageProviderStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstanceStorageProviderStatusDto>> TestStorageConnection(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var status = await _mediator.Send(new TestInstanceStorageProviderQuery(), cancellationToken);
        return Ok(status);
    }

    [HttpPost("storage/usage/recalculate", Name = RouteNames.RecalculateInstanceStorageUsage)]
    [EndpointSummary("Recalculate Storage Usage")]
    [EndpointDescription("Reconciles instance-wide storage usage counters from storage metadata. Requires instance administrator.")]
    [ProducesResponseType(typeof(InstanceStorageUsageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstanceStorageUsageDto>> RecalculateStorageUsage(CancellationToken cancellationToken = default)
    {
        if (!await IsInstanceAdminOrSetupAuthenticated(cancellationToken)) return this.ToForbiddenProblem(detail: "Instance administrator or active setup secret authority is required for this operation.");

        var usage = await _mediator.Send(new RecalculateInstanceStorageUsageCommand(), cancellationToken);
        return Ok(usage);
    }
}
