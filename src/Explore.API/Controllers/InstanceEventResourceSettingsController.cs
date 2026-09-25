using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/settings/instance/event-resources")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Tags("Settings")]
public sealed class InstanceEventResourceSettingsController(
    IAdminContext adminContext,
    IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto> resolveSettings,
    ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto> updateSettings,
    IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto> assembler) : SettingsCapabilityControllerBase
{
    [HttpGet(Name = RouteNames.GetInstanceEventResourceSettings)]
    [EndpointSummary("Get Instance Event Resource Settings")]
    [PrivateNoStore]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<SettingGroupResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<SettingGroupResponseDto>>> Get(CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
            return this.ToForbiddenProblem();

        var settings = await resolveSettings.QueryAsync(new ResolveSettingGroupQuery
        {
            Category = EventResourceSettingDefinitions.Category,
            Scope = SettingScope.Instance
        }, cancellationToken);
        return Ok(await assembler.ToResource(settings, HttpContext));
    }

    [HttpPut(Name = RouteNames.UpdateInstanceEventResourceSettings)]
    [EndpointSummary("Update Instance Event Resource Settings")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BatchUpdateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BatchUpdateResponseDto>> Put(
        [FromBody] UpdateSettingBatchDto body, CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
            return this.ToForbiddenProblem();
        if (body.Values is null || body.Values.Count > EventResourceSettingDefinitions.All.Count
            || body.Values.Any(item => item.Value is null || !EventResourceSettingMutationGuard.Handles(item.Key)))
            return this.ToValidationProblem(SettingsValidationProblem, "The resource policy update was rejected.");

        var result = await updateSettings.ExecuteAsync(new UpdateSettingBatchCommand
        {
            Category = EventResourceSettingDefinitions.Category,
            Scope = SettingScope.Instance,
            Values = body.Values,
            Mode = BatchUpdateMode.Strict
        }, cancellationToken);
        if (!result.Success)
        {
            // The batch handler may include rejected input in validation messages. Never reflect it.
            if (result.Results.Any(item => item.SkipReason == "setting_system_locked"))
                return this.ToCommandConflictProblem(Explore.Application.Responses.BaseCommandResponse.Failure<Guid>(
                    "setting_system_locked"), "Setting conflict", "The resource policy is locked.");
            return this.ToValidationProblem(SettingsValidationProblem, "The resource policy update was rejected.");
        }
        return Ok(result);
    }
}
