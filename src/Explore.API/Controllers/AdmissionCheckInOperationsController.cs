using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Hateoas;
using Explore.Application.Services.Registration;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Route("api/events/{eventId:guid}/admission/check-ins")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class AdmissionCheckInOperationsController(
    AdmissionCheckInOperationsService service,
    IAuthorizationProvider authorization,
    ITenantContext tenantContext) : EventControllerBase
{
    [HttpGet("health", Name = RouteNames.GetAdmissionCheckInHealth)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInHealthDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HalResource<AdmissionCheckInHealthDto>>> Health(
        Guid eventId,
        [FromQuery] Guid targetId,
        CancellationToken cancellationToken)
    {
        AdmissionCheckInHealthResult? result = await service.GetHealthAsync(
            new AdmissionCheckInHealthRequest(
                tenantContext.TenantId,
                eventId,
                targetId,
                RequiredUserId),
            cancellationToken);
        if (result is null)
            return GenericNotFound();

        bool canManage = await CanManageAsync(eventId, cancellationToken);
        return Ok(HealthResource(eventId, result, canManage));
    }

    [HttpPost("operations/stop", Name = RouteNames.StopAdmissionCheckIn)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInOperationalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<HalResource<AdmissionCheckInOperationalResultDto>>> Stop(
        Guid eventId,
        [FromBody] AdmissionCheckInOperationalRequestDto request,
        CancellationToken cancellationToken) =>
        Execute(eventId, request, AdmissionCheckInOperationalAction.Stop, cancellationToken);

    [HttpPost("operations/restore", Name = RouteNames.RestoreAdmissionCheckIn)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInOperationalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<HalResource<AdmissionCheckInOperationalResultDto>>> Restore(
        Guid eventId,
        [FromBody] AdmissionCheckInOperationalRequestDto request,
        CancellationToken cancellationToken) =>
        Execute(eventId, request, AdmissionCheckInOperationalAction.Restore, cancellationToken);

    [HttpPost("operations/reconcile", Name = RouteNames.ReconcileAdmissionCheckIn)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInOperationalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<HalResource<AdmissionCheckInOperationalResultDto>>> Reconcile(
        Guid eventId,
        [FromBody] AdmissionCheckInOperationalRequestDto request,
        CancellationToken cancellationToken) =>
        Execute(eventId, request, AdmissionCheckInOperationalAction.Reconcile, cancellationToken);

    private async Task<ActionResult<HalResource<AdmissionCheckInOperationalResultDto>>> Execute(
        Guid eventId,
        AdmissionCheckInOperationalRequestDto request,
        AdmissionCheckInOperationalAction action,
        CancellationToken cancellationToken)
    {
        AdmissionCheckInOperationalResult? result = await service.ExecuteAsync(
            new AdmissionCheckInOperationalRequest(
                tenantContext.TenantId,
                eventId,
                request.TargetId,
                RequiredUserId,
                action,
                request.ReasonCode),
            cancellationToken);
        return result is null
            ? GenericNotFound()
            : Ok(OperationResource(eventId, result));
    }

    internal HalResource<AdmissionCheckInHealthDto> HealthResource(
        Guid eventId,
        AdmissionCheckInHealthResult result,
        bool canManage)
    {
        var resource = new HalResource<AdmissionCheckInHealthDto>(new AdmissionCheckInHealthDto(
            result.TargetId,
            result.Status,
            result.InfrastructureStatus));
        resource.WithLink(LinkRelations.Self, HalLink.Create(Url.Link(
            RouteNames.GetAdmissionCheckInHealth,
            new { eventId, targetId = result.TargetId })!));
        if (canManage
            && result.InfrastructureStatus == AdmissionCheckInDependencyStatus.Available)
        {
            string routeName = result.Status == AdmissionCheckInOperationalStatus.Active
                ? RouteNames.StopAdmissionCheckIn
                : RouteNames.RestoreAdmissionCheckIn;
            string relation = result.Status == AdmissionCheckInOperationalStatus.Active
                ? LinkRelations.StopAdmissionCheckIn
                : LinkRelations.RestoreAdmissionCheckIn;
            resource.WithLink(relation, HalLink.CreateAction(
                Url.Link(routeName, new { eventId })!,
                HttpMethods.Post));
            resource.WithLink(LinkRelations.ReconcileAdmissionCheckIn, HalLink.CreateAction(
                Url.Link(RouteNames.ReconcileAdmissionCheckIn, new { eventId })!,
                HttpMethods.Post));
        }
        return resource;
    }

    private HalResource<AdmissionCheckInOperationalResultDto> OperationResource(
        Guid eventId,
        AdmissionCheckInOperationalResult result) =>
        new HalResource<AdmissionCheckInOperationalResultDto>(
                new AdmissionCheckInOperationalResultDto(
                    result.TargetId,
                    result.Action,
                    result.Status,
                    result.ReasonCode,
                    result.OccurredAtUtc))
            .WithLink(LinkRelations.AdmissionCheckInHealth, HalLink.Create(Url.Link(
                RouteNames.GetAdmissionCheckInHealth,
                new { eventId, targetId = result.TargetId })!));

    private async Task<bool> CanManageAsync(Guid eventId, CancellationToken cancellationToken)
    {
        AuthorizationDecision decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                ResourceKinds.Event,
                eventId.ToString("D"),
                AuthorizationActions.Events.EventCheckInManage,
                new AuthorizationScope(TenantId: tenantContext.TenantId.ToString("D")),
                new EventScopedAuthorizationFacts(tenantContext.TenantId, eventId),
                new AuthorizationSubject(RequiredUserId)),
            cancellationToken);
        return decision.IsAllowed;
    }

    private ObjectResult GenericNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Admission operation not found",
        detail: "The requested admission operation was not found.");
}
