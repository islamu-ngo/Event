using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Integrations;
using Explore.Application.Features.Integrations.Listmonk.Requests.Commands;
using Explore.Application.Features.Integrations.Listmonk.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/integrations/listmonk")]
[ApiController]
public sealed class ListmonkIntegrationSettingsController(IMediator mediator) : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor SettingsValidationProblem = new(
        "listmonkIntegrationSettings",
        "Listmonk integration settings validation failed",
        "Listmonk integration settings update failed.");

    private static readonly ApiValidationProblemDescriptor TestConnectionValidationProblem = new(
        "listmonkIntegrationConnection",
        "Listmonk integration connection validation failed",
        "Listmonk integration connection test failed.");

    private static readonly ApiValidationProblemDescriptor RecoveryValidationProblem = new(
        "integrationSyncRecovery",
        "Integration sync recovery validation failed",
        "Integration sync recovery failed.");

    [HttpGet("settings", Name = RouteNames.GetListmonkIntegrationSettings)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get Listmonk Integration Settings")]
    [EndpointDescription("Returns tenant Listmonk integration settings without exposing API credentials.")]
    [ProducesResponseType(typeof(ListmonkIntegrationSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListmonkIntegrationSettingsDto>> GetSettings(
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetListmonkIntegrationSettingsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPatch("settings", Name = RouteNames.UpdateListmonkIntegrationSettings)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EndpointSummary("Update Listmonk Integration Settings")]
    [EndpointDescription("Updates tenant-scoped non-secret Listmonk integration settings.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateSettings(
        [FromBody] UpdateListmonkIntegrationSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new UpdateListmonkIntegrationSettingsCommand { Dto = dto }, cancellationToken);
        if (result.IsSuccess)
            return Ok(result);

        return this.ToCommandValidationProblem(result, SettingsValidationProblem);
    }

    [HttpPost("test-connection", Name = RouteNames.TestListmonkIntegrationConnection)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EndpointSummary("Test Listmonk Integration Connection")]
    [EndpointDescription("Verifies that the configured tenant Listmonk API endpoint and credentials are usable.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> TestConnection(
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new TestListmonkConnectionCommand(), cancellationToken);
        if (result.IsSuccess)
            return Ok(result);

        return this.ToCommandValidationProblem(result, TestConnectionValidationProblem);
    }

    [HttpPost("queue/{outboxId:guid}/resolve", Name = RouteNames.ResolveIntegrationSyncAmbiguity)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EndpointSummary("Resolve Ambiguous Integration Sync")]
    [EndpointDescription("Applies an evidence-based accepted, definitely-not-accepted, or dead-letter decision without blind provider replay.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResolveAmbiguity(
        Guid outboxId,
        [FromBody] ResolveIntegrationSyncAmbiguityDto dto,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> result = await mediator.Send(
            new ResolveIntegrationSyncAmbiguityCommand(outboxId, dto),
            cancellationToken);
        return result.IsSuccess ? Ok(result) : this.ToCommandValidationProblem(result, RecoveryValidationProblem);
    }
}
