using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/settings")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Tags("Settings")]
public sealed class UserSettingsController(
    IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto> resolveSettings,
    ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>> updateSetting,
    ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto> updateSettings,
    ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>> resetSetting)
    : SettingsCapabilityControllerBase
{
    [HttpGet("user/{category}", Name = RouteNames.GetUserSettings)]
    [EndpointSummary("Get User Settings")]
    [EndpointDescription("Returns effective settings for the given category, resolved through the full hierarchy for the authenticated user.")]
    [ProducesResponseType(typeof(SettingGroupResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SettingGroupResponseDto>> GetUserSettings(
        string category, CancellationToken cancellationToken = default)
    {
        var result = await resolveSettings.QueryAsync(new ResolveSettingGroupQuery
        {
            Category = category,
            Scope = SettingScope.User
        }, cancellationToken);

        return Ok(result);
    }

    [HttpPut("user/{category}", Name = RouteNames.UpdateUserSettingsBatch)]
    [EndpointSummary("Batch Update User Settings")]
    [EndpointDescription("Applies multiple user preference updates for a category. Defaults to best-effort mode (skips locked settings, applies rest).")]
    [ProducesResponseType(typeof(BatchUpdateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BatchUpdateResponseDto>> UpdateUserSettingsBatch(
        string category,
        [FromBody] UpdateSettingBatchDto body,
        CancellationToken cancellationToken = default)
    {
        var result = await updateSettings.ExecuteAsync(new UpdateSettingBatchCommand
        {
            Category = category,
            Values = body.Values,
            Scope = SettingScope.User,
            Mode = body.Mode ?? BatchUpdateMode.BestEffort
        }, cancellationToken);

        if (!result.Success)
        {
            return this.ToValidationProblem(
                SettingsValidationProblem,
                result.Message ?? "User settings batch update failed.");
        }
        return Ok(result);
    }

    [HttpPut("user/keys/{key}", Name = RouteNames.UpdateUserSetting)]
    [EndpointSummary("Update Single User Setting")]
    [EndpointDescription("Updates a single user preference by key. Key must be a registered setting key.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateUserSetting(
        string key,
        [FromBody] UpdateSettingValueDto body,
        CancellationToken cancellationToken = default)
    {
        var response = await updateSetting.ExecuteAsync(new UpdateSettingCommand
        {
            Key = key,
            Value = body.Value,
            Scope = SettingScope.User
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    [HttpDelete("user/keys/{key}", Name = RouteNames.ResetUserSetting)]
    [EndpointSummary("Reset User Setting")]
    [EndpointDescription("Removes the user's override for a setting, restoring it to the next higher scope's value.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResetUserSetting(
        string key, CancellationToken cancellationToken = default)
    {
        var response = await resetSetting.ExecuteAsync(new ResetSettingCommand
        {
            Key = key,
            Scope = SettingScope.User
        }, cancellationToken);

        return HandleCommandResponse(response);
    }
}
