using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Tags("Settings")]
public sealed class InstanceAtprotoSettingsController(
    IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto> resolveSettings,
    ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>> updateSetting,
    ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>> resetSetting,
    ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>> lockSetting,
    ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>> unlockSetting,
    IAdminContext adminContext,
    IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto> settingGroupAssembler)
    : SettingsCapabilityControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor AtprotoAdministratorSettingNotFoundProblem = new(
        "ATProto administrator setting not found",
        "The requested ATProto administrator setting is not available.");

    [HttpGet("instance/atproto-federation", Name = RouteNames.GetInstanceAtprotoFederationSettings)]
    [EndpointSummary("Get Instance ATProto Federation Settings")]
    [EndpointDescription("Returns ATProto federation capability and validation profile at instance scope. Requires instance administrator.")]
    [EndpointClassification(EndpointClass.Admin)]
    [ProducesResponseType(typeof(HalResource<SettingGroupResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<SettingGroupResponseDto>>> GetInstanceSettings(
        CancellationToken cancellationToken = default)
    {
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            return this.ToForbiddenProblem(detail: "Instance administrator authority is required to view instance settings.");
        }

        var result = await resolveSettings.QueryAsync(new ResolveSettingGroupQuery
        {
            Category = AtprotoFederationSettingDefinitions.Category,
            Scope = SettingScope.Instance,
            IncludedKeys = AtprotoFederationSettingDefinitions.AdministratorKeys.ToHashSet(StringComparer.Ordinal)
        }, cancellationToken);

        return Ok(await settingGroupAssembler.ToResource(result, HttpContext));
    }

    [HttpPut("instance/atproto-federation/{key}", Name = RouteNames.UpdateInstanceAtprotoFederationSetting)]
    [EndpointSummary("Update Instance ATProto Federation Setting")]
    [EndpointDescription("Updates the ATProto capability or validation profile at instance scope. Requires instance administrator.")]
    [EndpointClassification(EndpointClass.Admin)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateInstanceSetting(
        string key,
        [FromBody] UpdateSettingValueDto body,
        CancellationToken cancellationToken = default)
    {
        if (!AtprotoFederationSettingDefinitions.IsAdministratorKey(key))
        {
            return this.ToNotFoundProblem(AtprotoAdministratorSettingNotFoundProblem);
        }

        var response = await updateSetting.ExecuteAsync(new UpdateSettingCommand
        {
            Key = key,
            Value = body.Value,
            Scope = SettingScope.Instance
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    [HttpDelete("instance/atproto-federation/{key}", Name = RouteNames.ResetInstanceAtprotoFederationSetting)]
    [EndpointSummary("Reset Instance ATProto Federation Setting")]
    [EndpointDescription("Removes an instance ATProto override and restores its registered default. Requires instance administrator.")]
    [EndpointClassification(EndpointClass.Admin)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResetInstanceSetting(
        string key, CancellationToken cancellationToken = default)
    {
        if (!AtprotoFederationSettingDefinitions.IsAdministratorKey(key))
        {
            return this.ToNotFoundProblem(AtprotoAdministratorSettingNotFoundProblem);
        }

        var response = await resetSetting.ExecuteAsync(new ResetSettingCommand
        {
            Key = key,
            Scope = SettingScope.Instance
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    [HttpPost("instance/atproto-federation/{key}/lock", Name = RouteNames.LockInstanceAtprotoFederationSetting)]
    [EndpointSummary("Lock Instance ATProto Federation Setting")]
    [EndpointDescription("Locks the ATProto capability or validation profile at instance scope. Requires instance administrator.")]
    [EndpointClassification(EndpointClass.Admin)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> LockInstanceSetting(
        string key, CancellationToken cancellationToken = default)
    {
        if (!AtprotoFederationSettingDefinitions.IsAdministratorKey(key))
        {
            return this.ToNotFoundProblem(AtprotoAdministratorSettingNotFoundProblem);
        }

        var response = await lockSetting.ExecuteAsync(new LockSettingCommand
        {
            Key = key,
            Scope = SettingScope.Instance
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    [HttpDelete("instance/atproto-federation/{key}/lock", Name = RouteNames.UnlockInstanceAtprotoFederationSetting)]
    [EndpointSummary("Unlock Instance ATProto Federation Setting")]
    [EndpointDescription("Unlocks the ATProto capability or validation profile at instance scope. Requires instance administrator.")]
    [EndpointClassification(EndpointClass.Admin)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UnlockInstanceSetting(
        string key, CancellationToken cancellationToken = default)
    {
        if (!AtprotoFederationSettingDefinitions.IsAdministratorKey(key))
        {
            return this.ToNotFoundProblem(AtprotoAdministratorSettingNotFoundProblem);
        }

        var response = await unlockSetting.ExecuteAsync(new UnlockSettingCommand
        {
            Key = key,
            Scope = SettingScope.Instance
        }, cancellationToken);

        return HandleCommandResponse(response);
    }
}
