using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Tags("Settings")]
public sealed class TenantSettingsController(
    IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto> resolveSettings,
    ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>> updateSetting,
    ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto> updateSettings,
    ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>> resetSetting,
    ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>> lockSetting,
    ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>> unlockSetting,
    IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto> settingGroupAssembler)
    : SettingsCapabilityControllerBase
{
    [HttpDelete("tenant/keys/{key}", Name = RouteNames.ResetTenantSetting)]
    [EndpointSummary("Reset Tenant Setting")]
    [EndpointDescription("Removes the tenant override for a setting, restoring the effective instance value. Requires tenant administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResetTenantSetting(
        string key, CancellationToken cancellationToken = default)
    {
        var response = await resetSetting.ExecuteAsync(new ResetSettingCommand
        {
            Key = key,
            Scope = SettingScope.Tenant
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    [HttpGet("tenant/{category}", Name = RouteNames.GetTenantScopedSettings)]
    [EndpointSummary("Get Tenant Settings")]
    [EndpointDescription("Returns effective settings for the given category at tenant scope. Requires tenant administrator.")]
    [PrivateNoStore]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<SettingGroupResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<SettingGroupResponseDto>>> GetTenantSettings(
        string category, CancellationToken cancellationToken = default)
    {
        var result = await resolveSettings.QueryAsync(new ResolveSettingGroupQuery
        {
            Category = category,
            Scope = SettingScope.Tenant
        }, cancellationToken);

        return Ok(await settingGroupAssembler.ToResource(result, HttpContext));
    }

    [HttpPut("tenant/{category}", Name = RouteNames.UpdateTenantSettingsBatch)]
    [EndpointSummary("Batch Update Tenant Settings")]
    [EndpointDescription("Applies multiple tenant setting updates for a category. Defaults to strict mode (rejects entire batch if any setting is locked). Requires tenant administrator.")]
    [ProducesResponseType(typeof(BatchUpdateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BatchUpdateResponseDto>> UpdateTenantSettingsBatch(
        string category,
        [FromBody] UpdateSettingBatchDto body,
        [FromServices] IOutputCacheStore outputCacheStore,
        CancellationToken cancellationToken = default)
    {
        var result = await updateSettings.ExecuteAsync(new UpdateSettingBatchCommand
        {
            Category = category,
            Values = body.Values,
            Scope = SettingScope.Tenant,
            Mode = body.Mode ?? BatchUpdateMode.Strict
        }, cancellationToken);

        if (!result.Success)
        {
            if (result.Message == CommandResponseResultMapper.VisitorAccessAccountRequiredConflict
                || result.Results.Any(item => item.SkipReason == CommandResponseResultMapper.VisitorAccessAccountRequiredConflict))
            {
                return this.MapCommandResponse(BaseCommandResponse.Failure<Guid>(
                    CommandResponseResultMapper.VisitorAccessAccountRequiredConflict));
            }

            return this.ToValidationProblem(
                SettingsValidationProblem,
                result.Message ?? "Tenant settings batch update failed.");
        }

        if (result.Success && result.Results.Any(result => result.Applied))
        {
            await outputCacheStore.EvictByTagAsync("public-experience-shell", cancellationToken);
        }
        return Ok(result);
    }

    [HttpPut("tenant/keys/{key}", Name = RouteNames.UpdateTenantSetting)]
    [EndpointSummary("Update Single Tenant Setting")]
    [EndpointDescription("Updates a single tenant setting by key. Requires tenant administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateTenantSetting(
        string key,
        [FromBody] UpdateSettingValueDto body,
        [FromServices] IOutputCacheStore outputCacheStore,
        CancellationToken cancellationToken = default)
    {
        var response = await updateSetting.ExecuteAsync(new UpdateSettingCommand
        {
            Key = key,
            Value = body.Value,
            Scope = SettingScope.Tenant
        }, cancellationToken);

        if (response.IsSuccess)
        {
            await outputCacheStore.EvictByTagAsync("public-experience-shell", cancellationToken);
        }

        return HandleCommandResponse(response);
    }

    [HttpPost("tenant/keys/{key}/lock", Name = RouteNames.LockTenantSetting)]
    [EndpointSummary("Lock Tenant Setting")]
    [EndpointDescription("Locks a setting at tenant scope, preventing lower-scope overrides from taking effect. Requires tenant administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> LockTenantSetting(
        string key, CancellationToken cancellationToken = default)
    {
        var response = await lockSetting.ExecuteAsync(new LockSettingCommand
        {
            Key = key,
            Scope = SettingScope.Tenant
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    [HttpDelete("tenant/keys/{key}/lock", Name = RouteNames.UnlockTenantSetting)]
    [EndpointSummary("Unlock Tenant Setting")]
    [EndpointDescription("Unlocks a setting at tenant scope, restoring the normal hierarchical cascade. Requires tenant administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UnlockTenantSetting(
        string key, CancellationToken cancellationToken = default)
    {
        var response = await unlockSetting.ExecuteAsync(new UnlockSettingCommand
        {
            Key = key,
            Scope = SettingScope.Tenant
        }, cancellationToken);

        return HandleCommandResponse(response);
    }
}
