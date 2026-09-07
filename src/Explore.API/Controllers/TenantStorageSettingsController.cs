using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenant/settings/storage")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class TenantStorageSettingsController(
    IMediator mediator,
    IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto> storageSettingsAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor PatchValidationProblem = new(
        "tenantStorageSettings",
        "Tenant storage settings validation failed",
        "Tenant storage settings patch failed.");

    [HttpGet("", Name = RouteNames.GetTenantStorageSettings)]
    [EndpointSummary("Get Tenant Storage Settings")]
    [EndpointDescription("Returns effective tenant storage policy, usage, lock state, and redacted optional S3 override settings.")]
    [ProducesResponseType(typeof(HalResource<TenantStorageSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<TenantStorageSettingsDto>>> GetStorageSettings(
        CancellationToken cancellationToken = default)
    {
        var settings = await mediator.Send(new GetTenantStorageSettingsQuery(), cancellationToken);
        var halResource = await storageSettingsAssembler.ToResource(settings, HttpContext);
        return Ok(halResource);
    }

    [HttpPatch("", Name = RouteNames.PatchTenantStorageSettings)]
    [EndpointSummary("Patch Tenant Storage Settings")]
    [EndpointDescription("Patches supplied current-tenant storage override leaves when instance storage delegation allows tenant edits.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PatchStorageSettings(
        [FromBody] PatchTenantStorageSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        var userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await mediator.Send(
            new PatchTenantStorageSettingsCommand
            {
                UserId = userId.Value,
                Settings = settings
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.FailureCode == FailureCodes.AdminRequired)
            {
                return this.ToForbiddenProblem(
                    detail: response.Message ?? "Tenant storage settings can only be patched by authorized administrators.");
            }

            return this.ToCommandValidationProblem(response, PatchValidationProblem);
        }

        return Ok(response);
    }

    [HttpPost("test", Name = RouteNames.TestTenantStorageConnection)]
    [EndpointSummary("Test Tenant Storage Connection")]
    [EndpointDescription("Tests the current tenant's effective storage provider, including an S3 write/delete probe when applicable.")]
    [ProducesResponseType(typeof(InstanceStorageProviderStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstanceStorageProviderStatusDto>> TestStorageConnection(
        CancellationToken cancellationToken = default)
    {
        var status = await mediator.Send(new TestTenantStorageProviderQuery(), cancellationToken);
        return Ok(status);
    }
}
