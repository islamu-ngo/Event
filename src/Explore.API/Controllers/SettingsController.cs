using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Filters;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
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
public class SettingsController : SettingsCapabilityControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor AtprotoAdministratorSettingNotFoundProblem = new(
        "ATProto administrator setting not found",
        "The requested ATProto administrator setting is not available.");

    private readonly IMediator _mediator;
    private readonly IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto> _resolveSettings;
    private readonly ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>> _updateSetting;
    private readonly ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>> _resetSetting;
    private readonly ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>> _lockSetting;
    private readonly ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>> _unlockSetting;
    private readonly IAdminContext _adminContext;
    private readonly IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto> _instanceSettingGroupAssembler;

    public SettingsController(
        IMediator mediator,
        IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto> resolveSettings,
        ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>> updateSetting,
        ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>> resetSetting,
        ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>> lockSetting,
        ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>> unlockSetting,
        IAdminContext adminContext,
        IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto> instanceSettingGroupAssembler)
    {
        _mediator = mediator;
        _resolveSettings = resolveSettings;
        _updateSetting = updateSetting;
        _resetSetting = resetSetting;
        _lockSetting = lockSetting;
        _unlockSetting = unlockSetting;
        _adminContext = adminContext;
        _instanceSettingGroupAssembler = instanceSettingGroupAssembler;
    }

    [HttpPost("email-delivery/disable-preview", Name = RouteNames.PreviewTenantSmtpDisable)]
    [EndpointSummary("Preview Tenant SMTP Disable")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
    [ProducesResponseType(typeof(HalResource<EmailDeliveryDisablePreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EmailDeliveryDisablePreviewDto>>> PreviewEmailDeliveryDisable(
        [FromServices] ITenantContext tenantContext,
        [FromServices] IResourceAssembler<EmailDeliveryDisablePreviewDto, EmailDeliveryDisablePreviewDto> assembler,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new PreviewEmailDeliveryDisableQuery(TenantId: tenantContext.TenantId), cancellationToken);
        return response.IsSuccess
            ? Ok(await assembler.ToResource(response.Id!, HttpContext))
            : this.ToEmailDeliveryDisableProblem(response);
    }

    [HttpPost("email-delivery/disable", Name = RouteNames.DisableTenantSmtp)]
    [EndpointSummary("Disable Tenant SMTP")]
    [PrivateNoStore]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DisableEmailDelivery(
        [FromBody] EmailDeliveryDisableRequest body,
        [FromServices] ITenantContext tenantContext, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DisableEmailDeliveryCommand(TenantId: tenantContext.TenantId,
            ExpectedRevision: body.ExpectedRevision, Acknowledgement: body.Acknowledgement,
            ConfirmationToken: body.ConfirmationToken), cancellationToken);
        return response.IsSuccess ? Ok(response) : this.ToEmailDeliveryDisableProblem(response);
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

        var result = await _resolveSettings.QueryAsync(new ResolveSettingGroupQuery
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

        var response = await _updateSetting.ExecuteAsync(new UpdateSettingCommand
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

        var response = await _resetSetting.ExecuteAsync(new ResetSettingCommand
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

        var response = await _lockSetting.ExecuteAsync(new LockSettingCommand
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

        var response = await _unlockSetting.ExecuteAsync(new UnlockSettingCommand
        {
            Key = key,
            Scope = SettingScope.Instance
        }, cancellationToken);

        return HandleCommandResponse(response);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

}
