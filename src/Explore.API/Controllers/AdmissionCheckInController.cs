using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Hateoas;
using Explore.Application.Services.Registration;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using AppBatchRequest = Explore.Application.Contracts.Admissions.AdmissionCheckInBatchRequest;
using AppCheckInRequest = Explore.Application.Contracts.Admissions.AdmissionCheckInRequest;
using ApiBatchRequest = Explore.Application.DTOs.Admissions.AdmissionCheckInBatchRequestDto;
using ApiCheckInRequest = Explore.Application.DTOs.Admissions.AdmissionCheckInRequestDto;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Route("api/events/{eventId:guid}/admission/check-ins")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class AdmissionCheckInController(
    AdmissionCheckInService service,
    AdmissionCheckInReportingService reportingService,
    IAuthorizationProvider authorization,
    ITenantContext tenantContext) : EventControllerBase
{
    [HttpPost("", Name = RouteNames.CheckInAdmission)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInResultDto>>> CheckIn(
        Guid eventId,
        [FromBody] ApiCheckInRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(eventId, PermissionCodes.EventCheckInManage, cancellationToken))
            return Forbidden();

        AdmissionCheckInResult result = await service.ProcessAsync(
            new AppCheckInRequest(
                tenantContext.TenantId,
                eventId,
                request.TargetId,
                request.Credential,
                AdmissionCheckInAction.CheckIn,
                null,
                RequiredUserId,
                null),
            cancellationToken);
        return result.Outcome == AdmissionCheckInOutcome.Rejected
            ? GenericNotFound()
            : Ok(CheckInResource(
                eventId,
                result,
                canCheckIn: true,
                canUndo: result.Outcome is AdmissionCheckInOutcome.CheckedIn or
                    AdmissionCheckInOutcome.AlreadyCheckedIn));
    }

    [HttpGet("{checkInId:guid}", Name = RouteNames.GetAdmissionCheckIn)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInResultDto>>> Get(
        Guid eventId,
        Guid checkInId,
        CancellationToken cancellationToken)
    {
        AdmissionCheckInDetail? detail = await reportingService.GetDetailAsync(
            new AdmissionCheckInDetailRequest(
                tenantContext.TenantId,
                eventId,
                checkInId,
                RequiredUserId),
            cancellationToken);
        if (detail is null)
            return GenericNotFound();

        bool canManage = await CanAsync(
            eventId,
            PermissionCodes.EventCheckInManage,
            cancellationToken);
        return Ok(CheckInResource(
            eventId,
            detail.Result,
            canCheckIn: canManage,
            canUndo: detail.CanUndo && canManage));
    }

    [HttpPost("batch", Name = RouteNames.BatchCheckInAdmissions)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInBatchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInBatchResultDto>>> Batch(
        Guid eventId,
        [FromBody] ApiBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(eventId, PermissionCodes.EventCheckInManage, cancellationToken))
            return Forbidden();

        AdmissionCheckInBatchResult result = await service.ProcessBatchAsync(
            new AppBatchRequest(
                tenantContext.TenantId,
                eventId,
                request.TargetId,
                RequiredUserId,
                null,
                request.Items.Select((item, index) => new AdmissionCheckInBatchItem(
                    index,
                    item.Credential,
                    AdmissionCheckInAction.CheckIn,
                    null)).ToArray()),
            cancellationToken);
        return result.Outcome == AdmissionCheckInBatchOutcome.BatchLimitExceeded
            ? Validation("Admission check-in batch must contain between 1 and 100 items.")
            : Ok(BatchResource(eventId, result));
    }

    [HttpPost("{checkInId:guid}/undo", Name = RouteNames.UndoAdmissionCheckIn)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInResultDto>>> Undo(
        Guid eventId,
        Guid checkInId,
        [FromBody] AdmissionCheckInUndoRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(eventId, PermissionCodes.EventCheckInManage, cancellationToken))
            return Forbidden();

        AdmissionCheckInResult result = await service.ProcessAsync(
            new AppCheckInRequest(
                tenantContext.TenantId,
                eventId,
                request.TargetId,
                request.Credential,
                AdmissionCheckInAction.Undo,
                request.ReasonCode,
                RequiredUserId,
                null,
                checkInId),
            cancellationToken);
        return result.Outcome == AdmissionCheckInOutcome.Rejected
            ? GenericNotFound()
            : Ok(CheckInResource(
                eventId,
                result,
                canCheckIn: true,
                canUndo: false));
    }

    [HttpGet("summary", Name = RouteNames.GetAdmissionCheckInSummary)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInSummaryDto>>> Summary(
        Guid eventId,
        [FromQuery, BindRequired] Guid targetId,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(eventId, PermissionCodes.EventCheckInView, cancellationToken))
            return Forbidden();

        AdmissionCheckInSummary? summary = await reportingService.GetSummaryAsync(
            new AdmissionCheckInSummaryRequest(
                tenantContext.TenantId,
                eventId,
                targetId,
                RequiredUserId),
            cancellationToken);
        return summary is null
            ? GenericNotFound()
            : Ok(SummaryResource(eventId, targetId, summary));
    }

    [HttpGet("audit", Name = RouteNames.GetAdmissionCheckInAudit)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInAuditPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInAuditPageDto>>> Audit(
        Guid eventId,
        [FromQuery, StringLength(34, MinimumLength = 34)] string? cursor,
        [FromQuery, Range(1, AdmissionCheckInReportingService.MaximumPageSize)] int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await CanAsync(eventId, PermissionCodes.EventCheckInView, cancellationToken))
            return Forbidden();

        AdmissionCheckInAuditPage? page = await reportingService.GetAuditPageAsync(
            new AdmissionCheckInAuditPageRequest(
                tenantContext.TenantId,
                eventId,
                RequiredUserId,
                cursor,
                pageSize),
            cancellationToken);
        return page is null
            ? GenericNotFound()
            : Ok(AuditResource(eventId, cursor, pageSize, page));
    }

    private async Task<bool> CanAsync(Guid eventId, string action, CancellationToken cancellationToken)
    {
        AuthorizationDecision decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            eventId.ToString("D"),
            action,
            new AuthorizationScope(TenantId: tenantContext.TenantId.ToString("D")),
            new EventScopedAuthorizationFacts(tenantContext.TenantId, eventId),
            new AuthorizationSubject(RequiredUserId)), cancellationToken);
        return decision.IsAllowed;
    }

    private ObjectResult Forbidden() => Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Forbidden",
        detail: "The requested admission operation is not available.");

    private ObjectResult GenericNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Admission operation not found",
        detail: "The requested admission operation was not found.");

    private ObjectResult Validation(string detail) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Admission request invalid",
        detail: detail);

    internal HalResource<AdmissionCheckInResultDto> CheckInResource(
        Guid eventId,
        AdmissionCheckInResult result,
        bool canCheckIn,
        bool canUndo)
    {
        var dto = new AdmissionCheckInResultDto(
            result.Outcome.ToString(),
            result.TargetId,
            result.OccurredAtUtc == default ? null : result.OccurredAtUtc,
            result.CheckInId);
        var resource = new HalResource<AdmissionCheckInResultDto>(dto);
        if (canCheckIn)
        {
            resource.WithLink(LinkRelations.CheckInAdmissions, HalLink.CreateAction(
                Url.Link(RouteNames.CheckInAdmission, new { eventId })!, HttpMethods.Post));
        }
        if (result.CheckInId is Guid checkInId)
        {
            resource.WithLink(LinkRelations.Self, HalLink.Create(
                Url.Link(RouteNames.GetAdmissionCheckIn, new { eventId, checkInId })!));
            if (canUndo)
            {
                resource.WithLink(LinkRelations.UndoAdmissionCheckIn, HalLink.CreateAction(
                    Url.Link(RouteNames.UndoAdmissionCheckIn, new { eventId, checkInId })!,
                    HttpMethods.Post));
            }
        }
        return resource;
    }

    private HalResource<AdmissionCheckInBatchResultDto> BatchResource(Guid eventId, AdmissionCheckInBatchResult result)
    {
        var dto = new AdmissionCheckInBatchResultDto(
            result.Outcome.ToString(),
            result.Items.Select(item => new AdmissionCheckInBatchItemResultDto(
                item.Index,
                item.Outcome.ToString(),
                item.TargetId,
                item.OccurredAtUtc == default ? null : item.OccurredAtUtc,
                item.CheckInId)).ToArray());
        HalResource<AdmissionCheckInResultDto>[] embedded = result.Items
            .Select(item => CheckInResource(
                eventId,
                new AdmissionCheckInResult(
                    item.Outcome,
                    item.TargetId,
                    item.OccurredAtUtc,
                    item.CheckInId),
                canCheckIn: true,
                canUndo: item.Outcome is AdmissionCheckInOutcome.CheckedIn or
                    AdmissionCheckInOutcome.AlreadyCheckedIn))
            .ToArray();
        return new HalResource<AdmissionCheckInBatchResultDto>(dto)
            .WithLink(LinkRelations.CheckInAdmissions, HalLink.CreateAction(
                Url.Link(RouteNames.BatchCheckInAdmissions, new { eventId })!, HttpMethods.Post))
            .WithEmbedded(LinkRelations.AdmissionCheckInResults, embedded);
    }

    private HalResource<AdmissionCheckInSummaryDto> SummaryResource(
        Guid eventId,
        Guid targetId,
        AdmissionCheckInSummary summary)
    {
        var dto = new AdmissionCheckInSummaryDto(
            summary.TargetType.ToString(),
            ResultCount(summary, AdmissionCheckInOutcome.CheckedIn),
            ResultCount(summary, AdmissionCheckInOutcome.Undone),
            StateCount(summary, AdmissionCheckInSummaryState.Active),
            StateCount(summary, AdmissionCheckInSummaryState.Inactive),
            summary.LastActivityTimeBucketUtc);
        return new HalResource<AdmissionCheckInSummaryDto>(dto)
            .WithLink(LinkRelations.Self, HalLink.Create(
                Url.Link(RouteNames.GetAdmissionCheckInSummary, new { eventId, targetId })!))
            .WithLink(LinkRelations.AdmissionCheckInAudit, HalLink.Create(
                Url.Link(RouteNames.GetAdmissionCheckInAudit, new { eventId, pageSize = 100 })!));
    }

    private HalResource<AdmissionCheckInAuditPageDto> AuditResource(
        Guid eventId,
        string? cursor,
        int pageSize,
        AdmissionCheckInAuditPage page)
    {
        var dto = new AdmissionCheckInAuditPageDto(
            page.Items.Select(item => new AdmissionCheckInAuditItemDto(
                item.Cursor,
                item.Action.ToString(),
                item.Outcome.ToString(),
                item.TargetType.ToString(),
                item.OccurredAtTimeBucketUtc)).ToArray(),
            page.NextCursor);
        var resource = new HalResource<AdmissionCheckInAuditPageDto>(dto)
            .WithLink(LinkRelations.Self, HalLink.Create(
                Url.Link(RouteNames.GetAdmissionCheckInAudit, new
                {
                    eventId,
                    cursor,
                    pageSize
                })!));
        if (page.NextCursor is string nextCursor)
        {
            resource.WithLink(LinkRelations.Next, HalLink.Create(
                Url.Link(RouteNames.GetAdmissionCheckInAudit, new { eventId, cursor = nextCursor, pageSize })!));
        }
        return resource;
    }

    private static long ResultCount(AdmissionCheckInSummary summary, AdmissionCheckInOutcome outcome) =>
        summary.ResultCounts.Single(item => item.Outcome == outcome).Count;

    private static long StateCount(AdmissionCheckInSummary summary, AdmissionCheckInSummaryState state) =>
        summary.StateCounts.Single(item => item.State == state).Count;
}

[ApiVersion("0.1")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
[Route("api/admission/scanner/check-ins")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class AdmissionScannerCheckInController(AdmissionCheckInService service) : ControllerBase
{
    [HttpPost("", Name = RouteNames.ScannerCheckInAdmission)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.AdmissionScanner)]
    [PrivateNoStore]
    [RequireIdempotencyKey]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionScannerCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInResultDto>>> CheckIn(
        [FromBody] AdmissionScannerCheckInRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryScope(AdmissionCheckInAction.CheckIn, out AdmissionScannerRequestScope scope))
            return GenericNotFound();
        AdmissionCheckInResult result = await Process(
            scope, request.Credential, AdmissionCheckInAction.CheckIn, null, null, cancellationToken);
        return result.Outcome == AdmissionCheckInOutcome.Rejected
            ? GenericNotFound()
            : Ok(Resource(result));
    }

    [HttpPost("batch", Name = RouteNames.ScannerBatchCheckInAdmissions)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.AdmissionScanner)]
    [PrivateNoStore]
    [RequireIdempotencyKey]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionScannerCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInBatchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInBatchResultDto>>> Batch(
        [FromBody] AdmissionScannerCheckInBatchRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryScope(AdmissionCheckInAction.CheckIn, out AdmissionScannerRequestScope scope))
            return GenericNotFound();
        AdmissionCheckInBatchResult result = await service.ProcessBatchAsync(new AppBatchRequest(
            scope.TenantId,
            scope.EventId,
            scope.TargetId,
            null,
            scope.CapabilityId,
            request.Items.Select((item, index) => new AdmissionCheckInBatchItem(
                index,
                item.Credential,
                AdmissionCheckInAction.CheckIn,
                null)).ToArray()), cancellationToken);
        if (result.Outcome == AdmissionCheckInBatchOutcome.BatchLimitExceeded)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Admission request invalid", detail: "Admission check-in batch must contain between 1 and 100 items.");
        var dto = new AdmissionCheckInBatchResultDto(
            result.Outcome.ToString(),
            result.Items.Select(item => new AdmissionCheckInBatchItemResultDto(
                item.Index, item.Outcome.ToString(), item.TargetId,
                item.OccurredAtUtc == default ? null : item.OccurredAtUtc,
                item.CheckInId)).ToArray());
        HalResource<AdmissionCheckInResultDto>[] embedded = result.Items
            .Select(item => Resource(new AdmissionCheckInResult(
                item.Outcome,
                item.TargetId,
                item.OccurredAtUtc,
                item.CheckInId)))
            .ToArray();
        return Ok(new HalResource<AdmissionCheckInBatchResultDto>(dto)
            .WithLink(LinkRelations.CheckInAdmissions, HalLink.CreateAction(
                "/api/admission/scanner/check-ins/batch",
                HttpMethods.Post))
            .WithEmbedded(LinkRelations.AdmissionCheckInResults, embedded));
    }

    [HttpPost("{checkInId:guid}/undo", Name = RouteNames.ScannerUndoAdmissionCheckIn)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.AdmissionScanner)]
    [PrivateNoStore]
    [RequireIdempotencyKey]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionScannerCheckInPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionCheckInResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionCheckInResultDto>>> Undo(
        Guid checkInId,
        [FromBody] AdmissionScannerCheckInUndoRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryScope(AdmissionCheckInAction.Undo, out AdmissionScannerRequestScope scope))
            return GenericNotFound();
        AdmissionCheckInResult result = await Process(
            scope, request.Credential, AdmissionCheckInAction.Undo, request.ReasonCode, checkInId, cancellationToken);
        return result.Outcome == AdmissionCheckInOutcome.Rejected
            ? GenericNotFound()
            : Ok(Resource(result));
    }

    private bool TryScope(AdmissionCheckInAction action, out AdmissionScannerRequestScope scope) =>
        User.TryGetAdmissionScannerScope(action, out scope);

    private Task<AdmissionCheckInResult> Process(
        AdmissionScannerRequestScope scope,
        string credential,
        AdmissionCheckInAction action,
        Explore.Domain.Enums.AdmissionCheckInUndoReasonCodeEnum? reasonCode,
        Guid? checkInId,
        CancellationToken cancellationToken) => service.ProcessAsync(new AppCheckInRequest(
            scope.TenantId,
            scope.EventId,
            scope.TargetId,
            credential,
            action,
            reasonCode,
            null,
            scope.CapabilityId,
            checkInId), cancellationToken);

    internal HalResource<AdmissionCheckInResultDto> Resource(AdmissionCheckInResult result)
    {
        var resource = new HalResource<AdmissionCheckInResultDto>(new AdmissionCheckInResultDto(
                result.Outcome.ToString(), result.TargetId,
                result.OccurredAtUtc == default ? null : result.OccurredAtUtc,
                result.CheckInId));
        if (User.TryGetAdmissionScannerScope(
                AdmissionCheckInAction.CheckIn,
                out _))
        {
            resource.WithLink(LinkRelations.CheckInAdmissions, HalLink.CreateAction(
                "/api/admission/scanner/check-ins", HttpMethods.Post));
        }
        if (result.CheckInId is Guid checkInId
            && result.Outcome is (AdmissionCheckInOutcome.CheckedIn or
                AdmissionCheckInOutcome.AlreadyCheckedIn)
            && User.TryGetAdmissionScannerScope(
                AdmissionCheckInAction.Undo,
                out _))
        {
            resource.WithLink(LinkRelations.UndoAdmissionCheckIn, HalLink.CreateAction(
                $"/api/admission/scanner/check-ins/{checkInId:D}/undo",
                HttpMethods.Post));
        }
        return resource;
    }

    private ObjectResult GenericNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Admission operation not found",
        detail: "The requested admission operation was not found.");
}
