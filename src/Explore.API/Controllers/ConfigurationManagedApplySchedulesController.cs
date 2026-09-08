namespace Explore.API.Controllers;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ConfigurationImport;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManagedApplyScheduleRequestDto(
    Guid SessionId,
    DateTime ApplyNotBefore,
    DateTime ApplyBefore);

[ApiController]
[ApiVersion("0.1")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Route("api/configuration-import/managed-schedules")]
[Tags("Configuration Import")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
[RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
public sealed class ConfigurationManagedApplySchedulesController(
    ConfigurationManagedApplyScheduleService schedules,
    IAuthorizationProvider authorization)
    : ConfigurationImportSessionsControllerBase
{
    [HttpPost("instance", Name = RouteNames.CreateInstanceConfigurationManagedApplySchedule)]
    [ProducesResponseType(typeof(ConfigurationManagedApplyScheduleResult), StatusCodes.Status201Created)]
    public Task<ActionResult<ConfigurationManagedApplyScheduleResult>> CreateInstance(
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)]
        [Required]
        string accessToken,
        [FromBody] ConfigurationManagedApplyScheduleRequestDto request,
        CancellationToken cancellationToken) =>
        CreateAsync(
            ConfigurationImportTarget.ForInstance(),
            accessToken,
            request,
            cancellationToken);

    [HttpPost("tenants/{tenantId:guid}", Name = RouteNames.CreateTenantConfigurationManagedApplySchedule)]
    [ProducesResponseType(typeof(ConfigurationManagedApplyScheduleResult), StatusCodes.Status201Created)]
    public Task<ActionResult<ConfigurationManagedApplyScheduleResult>> CreateTenant(
        Guid tenantId,
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)]
        [Required]
        string accessToken,
        [FromBody] ConfigurationManagedApplyScheduleRequestDto request,
        CancellationToken cancellationToken) =>
        CreateAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            accessToken,
            request,
            cancellationToken);

    [HttpPost("instance/{scheduleId:guid}/approval", Name = RouteNames.ApproveInstanceConfigurationManagedApplySchedule)]
    [ProducesResponseType(typeof(ConfigurationManagedApplyScheduleResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationManagedApplyScheduleResult>> ApproveInstance(
        Guid scheduleId,
        CancellationToken cancellationToken) =>
        ApproveAsync(
            ConfigurationImportTarget.ForInstance(),
            scheduleId,
            cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/{scheduleId:guid}/approval", Name = RouteNames.ApproveTenantConfigurationManagedApplySchedule)]
    [ProducesResponseType(typeof(ConfigurationManagedApplyScheduleResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationManagedApplyScheduleResult>> ApproveTenant(
        Guid tenantId,
        Guid scheduleId,
        CancellationToken cancellationToken) =>
        ApproveAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            scheduleId,
            cancellationToken);

    [HttpDelete("instance/{scheduleId:guid}", Name = RouteNames.CancelInstanceConfigurationManagedApplySchedule)]
    [ProducesResponseType(typeof(ConfigurationManagedApplyScheduleResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationManagedApplyScheduleResult>> CancelInstance(
        Guid scheduleId,
        CancellationToken cancellationToken) =>
        CancelAsync(
            ConfigurationImportTarget.ForInstance(),
            scheduleId,
            cancellationToken);

    [HttpDelete("tenants/{tenantId:guid}/{scheduleId:guid}", Name = RouteNames.CancelTenantConfigurationManagedApplySchedule)]
    [ProducesResponseType(typeof(ConfigurationManagedApplyScheduleResult), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationManagedApplyScheduleResult>> CancelTenant(
        Guid tenantId,
        Guid scheduleId,
        CancellationToken cancellationToken) =>
        CancelAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            scheduleId,
            cancellationToken);

    private async Task<ActionResult<ConfigurationManagedApplyScheduleResult>>
        CreateAsync(
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationManagedApplyScheduleRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(
                authorization,
                target.TenantId,
                cancellationToken))
        {
            return Forbidden();
        }
        ConfigurationManagedApplyScheduleResult created =
            await schedules.CreateAsync(
                request.SessionId,
                target,
                accessToken,
                request.ApplyNotBefore,
                request.ApplyBefore,
                cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    private async Task<ActionResult<ConfigurationManagedApplyScheduleResult>>
        ApproveAsync(
        ConfigurationImportTarget target,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(
                authorization,
                target.TenantId,
                cancellationToken))
        {
            return Forbidden();
        }
        return Ok(await schedules.ApproveAsync(
            scheduleId,
            target,
            cancellationToken));
    }

    private async Task<ActionResult<ConfigurationManagedApplyScheduleResult>>
        CancelAsync(
        ConfigurationImportTarget target,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(
                authorization,
                target.TenantId,
                cancellationToken))
        {
            return Forbidden();
        }
        return Ok(await schedules.CancelAsync(
            scheduleId,
            target,
            cancellationToken));
    }

    private ObjectResult Forbidden() => Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Managed configuration schedule unavailable",
        detail: "The current target authority does not permit this operation.");
}
