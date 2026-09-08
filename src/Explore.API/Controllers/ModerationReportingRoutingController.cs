using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenant/settings/moderation-reporting/routing-state")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class ModerationReportingRoutingController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<ReportingRoutingStateDto, ReportingRoutingStateDto> routingStateAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "moderationReportingRoutingSettings",
        "Moderation reporting routing settings validation failed",
        "Moderation reporting routing settings update failed.");

    private static readonly ApiValidationProblemDescriptor ProviderTestValidationProblem = new(
        "moderationReportingProviderTest",
        "Moderation reporting provider test failed",
        "Moderation reporting provider test failed.");

    [HttpGet("", Name = RouteNames.GetModerationReportingRoutingState)]
    [EndpointSummary("Get Moderation Reporting Routing State")]
    [EndpointDescription("Returns the current tenant's effective moderation reporting provider routing state with all secrets redacted.")]
    [ProducesResponseType(typeof(HalResource<ReportingRoutingStateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<ReportingRoutingStateDto>>> GetRoutingState(
        CancellationToken cancellationToken = default)
    {
        var routingState = await mediator.Send(
            new GetReportingRoutingStateRequest(tenantContext.TenantId),
            cancellationToken);

        var halResource = await routingStateAssembler.ToResource(routingState, HttpContext);
        return Ok(halResource);
    }

    [HttpPatch("", Name = RouteNames.UpdateModerationReportingRoutingSettings)]
    [EndpointSummary("Update Moderation Reporting Routing Settings")]
    [EndpointDescription("Updates current-tenant moderation reporting provider overrides when instance policy allows tenant edits.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateRoutingSettings(
        [FromBody] UpdateReportingRoutingSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        var userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await mediator.Send(
            new UpdateReportingRoutingSettingsCommand(tenantContext.TenantId, userId.Value, settings),
            cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.FailureCode is FailureCodes.ReportingTenantOverridesLocked or FailureCodes.AdminRequired)
            {
                return this.ToForbiddenProblem(
                    detail: response.Message ?? "Moderation reporting routing settings can only be updated by authorized administrators.");
            }

            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [HttpPost("test/{provider}", Name = RouteNames.TestModerationReportingProvider)]
    [EndpointSummary("Test Moderation Reporting Provider")]
    [EndpointDescription("Checks whether the current tenant provider target is ready for reporting dispatch without returning provider secrets.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> TestProvider(
        [FromRoute] EventReportExternalProvider provider,
        CancellationToken cancellationToken = default)
    {
        var userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await mediator.Send(
            new TestReportingProviderTargetCommand(tenantContext.TenantId, userId.Value, provider),
            cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.FailureCode is FailureCodes.ReportingTenantOverridesLocked or FailureCodes.AdminRequired)
            {
                return this.ToForbiddenProblem(
                    detail: response.Message ?? "Moderation reporting providers can only be tested by authorized administrators.");
            }

            return this.ToCommandValidationProblem(response, ProviderTestValidationProblem);
        }

        return Ok(response);
    }
}
