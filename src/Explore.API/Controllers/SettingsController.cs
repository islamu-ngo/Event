using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/settings")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public class SettingsController : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor SettingsValidationProblem = new(
        "settings",
        "Settings validation failed",
        "Settings update failed.");

    private static readonly ApiNotFoundProblemDescriptor AtprotoAdministratorSettingNotFoundProblem = new(
        "ATProto administrator setting not found",
        "The requested ATProto administrator setting is not available.");

    private readonly IMediator _mediator;
    private readonly IAdminContext _adminContext;
    private readonly IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto> _instanceSettingGroupAssembler;

    public SettingsController(
        IMediator mediator,
        IAdminContext adminContext,
        IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto> instanceSettingGroupAssembler)
    {
        _mediator = mediator;
        _adminContext = adminContext;
        _instanceSettingGroupAssembler = instanceSettingGroupAssembler;
    }

    // ── User Scope Endpoints ────────────────────────────────────────────

    [HttpGet("user/{category}", Name = RouteNames.GetUserSettings)]
    [EndpointSummary("Get User Settings")]
    [EndpointDescription("Returns effective settings for the given category, resolved through the full hierarchy for the authenticated user.")]
    [ProducesResponseType(typeof(SettingGroupResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SettingGroupResponseDto>> GetUserSettings(
        string category, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ResolveSettingGroupQuery
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
        var result = await _mediator.Send(new UpdateSettingBatchCommand
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
        var response = await _mediator.Send(new UpdateSettingCommand
        {
            Key = key,
            Value = body.Value,
            Scope = SettingScope.User
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    [HttpDelete("tenant/keys/{key}", Name = RouteNames.ResetTenantSetting)]
    [EndpointSummary("Reset Tenant Setting")]
    [EndpointDescription("Removes the tenant override for a setting, restoring the effective instance value. Requires tenant administrator.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResetTenantSetting(
        string key, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ResetSettingCommand
        {
            Key = key,
            Scope = SettingScope.Tenant
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
        var response = await _mediator.Send(new ResetSettingCommand
        {
            Key = key,
            Scope = SettingScope.User
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    // ── Tenant Scope Endpoints ──────────────────────────────────────────

    [HttpGet("tenant/{category}", Name = RouteNames.GetTenantScopedSettings)]
    [EndpointSummary("Get Tenant Settings")]
    [EndpointDescription("Returns effective settings for the given category at tenant scope. Requires tenant administrator.")]
    [ProducesResponseType(typeof(SettingGroupResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SettingGroupResponseDto>> GetTenantSettings(
        string category, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ResolveSettingGroupQuery
        {
            Category = category,
            Scope = SettingScope.Tenant
        }, cancellationToken);

        return Ok(result);
    }

    [HttpPut("tenant/{category}", Name = RouteNames.UpdateTenantSettingsBatch)]
    [EndpointSummary("Batch Update Tenant Settings")]
    [EndpointDescription("Applies multiple tenant setting updates for a category. Defaults to strict mode (rejects entire batch if any setting is locked). Requires tenant administrator.")]
    [ProducesResponseType(typeof(BatchUpdateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BatchUpdateResponseDto>> UpdateTenantSettingsBatch(
        string category,
        [FromBody] UpdateSettingBatchDto body,
        [FromServices] IOutputCacheStore outputCacheStore,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new UpdateSettingBatchCommand
        {
            Category = category,
            Values = body.Values,
            Scope = SettingScope.Tenant,
            Mode = body.Mode ?? BatchUpdateMode.Strict
        }, cancellationToken);

        if (!result.Success)
        {
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
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateTenantSetting(
        string key,
        [FromBody] UpdateSettingValueDto body,
        [FromServices] IOutputCacheStore outputCacheStore,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UpdateSettingCommand
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
    public async Task<ActionResult<BaseCommandResponse<Guid>>> LockTenantSetting(
        string key, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new LockSettingCommand
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
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UnlockTenantSetting(
        string key, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UnlockSettingCommand
        {
            Key = key,
            Scope = SettingScope.Tenant
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

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
        if (!await _adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            return this.ToForbiddenProblem(detail: "Instance administrator authority is required to view instance settings.");
        }

        var result = await _mediator.Send(new ResolveSettingGroupQuery
        {
            Category = AtprotoFederationSettingDefinitions.Category,
            Scope = SettingScope.Instance,
            IncludedKeys = AtprotoFederationSettingDefinitions.AdministratorKeys.ToHashSet(StringComparer.Ordinal)
        }, cancellationToken);

        return Ok(await _instanceSettingGroupAssembler.ToResource(result, HttpContext));
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

        var response = await _mediator.Send(new UpdateSettingCommand
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

        var response = await _mediator.Send(new ResetSettingCommand
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

        var response = await _mediator.Send(new LockSettingCommand
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

        var response = await _mediator.Send(new UnlockSettingCommand
        {
            Key = key,
            Scope = SettingScope.Instance
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private ActionResult<BaseCommandResponse<Guid>> HandleCommandResponse(BaseCommandResponse<Guid> response)
    {
        if (response.IsSuccess) return Ok(response);

        if (response.FailureCode == FailureCodes.AdminRequired)
        {
            return this.ToForbiddenProblem(detail: response.Message);
        }

        return this.ToCommandValidationProblem(response, SettingsValidationProblem);
    }

}
