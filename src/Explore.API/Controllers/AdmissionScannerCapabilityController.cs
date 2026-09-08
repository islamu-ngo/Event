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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Route("api/events/{eventId:guid}/admission/scanner-capabilities")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class AdmissionScannerCapabilityController(
    AdmissionScannerCapabilityService service,
    IAuthorizationProvider authorization,
    ITenantContext tenantContext) : EventControllerBase
{
    [HttpGet("", Name = RouteNames.ListAdmissionScannerCapabilities)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ProducesResponseType(typeof(HalCollectionResource<AdmissionScannerCapabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalCollectionResource<AdmissionScannerCapabilityDto>>> List(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(eventId, cancellationToken)) return Forbidden();
        IReadOnlyList<AdmissionScannerCapabilityDescriptor> descriptors = await service.ListAsync(
            tenantContext.TenantId, eventId, cancellationToken);
        HalResource<AdmissionScannerCapabilityDto>[] resources = descriptors.Select(descriptor =>
            Resource(Map(descriptor), eventId)).ToArray();
        return Ok(HalCollectionResource<AdmissionScannerCapabilityDto>.Create(
            resources,
            1,
            Math.Max(1, resources.Length),
            resources.Length,
            new Dictionary<string, HalLink>
            {
                [LinkRelations.Self] = HalLink.Create(Url.Link(
                    RouteNames.ListAdmissionScannerCapabilities, new { eventId })!),
                [LinkRelations.IssueScannerCapability] = HalLink.CreateAction(Url.Link(
                    RouteNames.IssueAdmissionScannerCapability, new { eventId })!, HttpMethods.Post)
            }));
    }

    [HttpPost("", Name = RouteNames.IssueAdmissionScannerCapability)]
    [SuppressIdempotencyResponseStorage]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionScannerCapabilityPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionScannerCapabilityIssuedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionScannerCapabilityIssuedDto>>> Issue(
        Guid eventId,
        [FromBody] IssueAdmissionScannerCapabilityRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(eventId, cancellationToken)) return Forbidden();
        AdmissionScannerCapabilityIssuedResult result;
        try
        {
            result = await service.IssueAsync(new AdmissionScannerCapabilityIssueRequest(
                request.IssueRequestId,
                tenantContext.TenantId,
                eventId,
                request.TargetId,
                request.Actions,
                request.DeviceLabel,
                request.ExpiresAt,
                RequiredUserId), cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        if (result.Outcome == AdmissionScannerCapabilityIssueOutcome.Rejected ||
            result.Descriptor is null)
            return GenericNotFound();
        AdmissionScannerCapabilityDescriptor descriptor = result.Descriptor;
        var dto = new AdmissionScannerCapabilityIssuedDto(
            descriptor.ScannerCapabilityId,
            descriptor.EventId,
            descriptor.TargetId,
            descriptor.Actions,
            descriptor.DeviceLabel,
            descriptor.ExpiresAtUtc,
            descriptor.RevokedAtUtc,
            descriptor.MaskedCapability,
            result.Outcome == AdmissionScannerCapabilityIssueOutcome.Issued
                ? result.PlaintextCapability
                : null);
        return Ok(IssuedResource(dto, eventId));
    }

    [HttpDelete("{scannerCapabilityId:guid}", Name = RouteNames.RevokeAdmissionScannerCapability)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionScannerCapabilityPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HalResource<AdmissionScannerCapabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<HalResource<AdmissionScannerCapabilityDto>>> Revoke(
        Guid eventId,
        Guid scannerCapabilityId,
        [FromBody] RevokeAdmissionScannerCapabilityRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(eventId, cancellationToken)) return Forbidden();
        AdmissionScannerCapabilityRevocationResult result;
        try
        {
            result = await service.RevokeAsync(new AdmissionScannerCapabilityRevokeRequest(
                tenantContext.TenantId,
                eventId,
                scannerCapabilityId,
                RequiredUserId,
                request.Reason), cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        if (result.Outcome == AdmissionScannerCapabilityRevocationOutcome.Rejected)
            return GenericNotFound();
        try
        {
            AdmissionScannerCapabilityDescriptor descriptor = await service.ReadAsync(
                new AdmissionScannerCapabilityReadRequest(tenantContext.TenantId, scannerCapabilityId),
                cancellationToken);
            return descriptor.EventId == eventId
                ? Ok(Resource(Map(descriptor), eventId))
                : GenericNotFound();
        }
        catch (KeyNotFoundException)
        {
            return GenericNotFound();
        }
    }

    private async Task<bool> CanManageAsync(Guid eventId, CancellationToken cancellationToken)
    {
        AuthorizationDecision decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            eventId.ToString("D"),
            AuthorizationActions.Events.ManageTickets,
            new AuthorizationScope(TenantId: tenantContext.TenantId.ToString("D")),
            new EventScopedAuthorizationFacts(tenantContext.TenantId, eventId),
            new AuthorizationSubject(RequiredUserId)), cancellationToken);
        return decision.IsAllowed;
    }

    private HalResource<AdmissionScannerCapabilityDto> Resource(
        AdmissionScannerCapabilityDto dto,
        Guid eventId) => new HalResource<AdmissionScannerCapabilityDto>(dto)
        .WithLink(LinkRelations.Self, HalLink.Create(Url.Link(
            RouteNames.ListAdmissionScannerCapabilities, new { eventId })!))
        .WithLink(LinkRelations.RevokeScannerCapability, HalLink.CreateAction(Url.Link(
            RouteNames.RevokeAdmissionScannerCapability,
            new { eventId, scannerCapabilityId = dto.ScannerCapabilityId })!, HttpMethods.Delete));

    private HalResource<AdmissionScannerCapabilityIssuedDto> IssuedResource(
        AdmissionScannerCapabilityIssuedDto dto,
        Guid eventId) => new HalResource<AdmissionScannerCapabilityIssuedDto>(dto)
        .WithLink(LinkRelations.Collection, HalLink.Create(Url.Link(
            RouteNames.ListAdmissionScannerCapabilities, new { eventId })!))
        .WithLink(LinkRelations.RevokeScannerCapability, HalLink.CreateAction(Url.Link(
            RouteNames.RevokeAdmissionScannerCapability,
            new { eventId, scannerCapabilityId = dto.ScannerCapabilityId })!, HttpMethods.Delete));

    private static AdmissionScannerCapabilityDto Map(AdmissionScannerCapabilityDescriptor descriptor) => new(
        descriptor.ScannerCapabilityId,
        descriptor.EventId,
        descriptor.TargetId,
        descriptor.Actions,
        descriptor.DeviceLabel,
        descriptor.ExpiresAtUtc,
        descriptor.RevokedAtUtc,
        descriptor.MaskedCapability);

    private ObjectResult Forbidden() => Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Forbidden",
        detail: "The requested scanner capability operation is not available.");

    private ObjectResult GenericNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Scanner capability not found",
        detail: "The requested scanner capability was not found.");

    private ObjectResult Validation(string detail) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Scanner capability request invalid",
        detail: detail);
}
