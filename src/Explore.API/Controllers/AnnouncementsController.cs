using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.Announcements.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Settings;
using ISLAMU.Wire.Contracts.Announcements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

public enum AnnouncementSourceTarget { Instance, Tenant, ManagedTenant }

/// <summary>The server-selected editing context distinguishes tenant and control-plane affordances.</summary>
public sealed record AnnouncementSourceResource(AnnouncementSourceDto Source, AnnouncementSourceTarget Target);

[ApiController]
[ApiVersion("0.1")]
[Route("api/announcements")]
[Tags("Announcements")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[OutputCache(NoStore = true)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
public sealed class AnnouncementsController(
    IQueryHandler<GetInstanceAnnouncementSourceQuery, AnnouncementSourceSnapshot> instanceSource,
    IQueryHandler<GetTenantAnnouncementSourceQuery, AnnouncementSourceSnapshot> tenantSource,
    IQueryHandler<GetManagedTenantAnnouncementSourceQuery, AnnouncementSourceSnapshot> managedSource,
    IQueryHandler<GetMyAnnouncementDismissalQuery, AnnouncementDismissalState> selfQuery,
    ICommandHandler<UpdateInstanceAnnouncementCommand, AnnouncementPublicationResult> updateInstance,
    ICommandHandler<UpdateTenantAnnouncementCommand, AnnouncementPublicationResult> updateTenant,
    ICommandHandler<UpdateManagedTenantAnnouncementCommand, AnnouncementPublicationResult> updateManaged,
    ICommandHandler<DismissAnnouncementCommand, AnnouncementDismissalResult> dismiss,
    IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery,
    ITenantContext tenantContext,
    IResourceAssembler<AnnouncementSourceResource, AnnouncementSourceResource> sourceAssembler,
    IResourceAssembler<AnnouncementSelfStateDto, AnnouncementSelfStateDto> selfAssembler,
    IResourceAssembler<AnnouncementPublicationReceiptDto, AnnouncementPublicationReceiptDto> publicationAssembler,
    IResourceAssembler<AnnouncementDismissalReceiptDto, AnnouncementDismissalReceiptDto> dismissalAssembler,
    IOutputCacheStore cacheStore) : ControllerBase
{
    [HttpGet("instance", Name = RouteNames.GetInstanceAnnouncementSource)]
    [EndpointSummary("Get the original instance announcement source")]
    [ProducesResponseType(typeof(HalResource<AnnouncementSourceResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementSourceResource>>> GetInstanceSource(
        CancellationToken cancellationToken = default)
    {
        var source = await instanceSource.QueryAsync(new GetInstanceAnnouncementSourceQuery(), cancellationToken);
        return Ok(await sourceAssembler.ToResource(
            new(AnnouncementTransportMapper.ToDto(source), AnnouncementSourceTarget.Instance), HttpContext));
    }

    [HttpGet("tenant", Name = RouteNames.GetTenantAnnouncementSource)]
    [EndpointSummary("Get the current tenant's original announcement source")]
    [ProducesResponseType(typeof(HalResource<AnnouncementSourceResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementSourceResource>>> GetTenantSource(
        CancellationToken cancellationToken = default)
    {
        var source = await tenantSource.QueryAsync(
            new GetTenantAnnouncementSourceQuery(tenantContext.TenantId), cancellationToken);
        return Ok(await sourceAssembler.ToResource(
            new(AnnouncementTransportMapper.ToDto(source), AnnouncementSourceTarget.Tenant), HttpContext));
    }

    [HttpGet("tenants/{tenantId:guid}", Name = RouteNames.GetManagedTenantAnnouncementSource)]
    [EndpointSummary("Get an instance-managed tenant's original announcement source")]
    [ProducesResponseType(typeof(HalResource<AnnouncementSourceResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementSourceResource>>> GetManagedTenantSource(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new BadRequestException("An actual managed tenant identity is required.");
        var source = await managedSource.QueryAsync(new GetManagedTenantAnnouncementSourceQuery(tenantId), cancellationToken);
        return Ok(await sourceAssembler.ToResource(
            new(AnnouncementTransportMapper.ToDto(source), AnnouncementSourceTarget.ManagedTenant), HttpContext));
    }

    [HttpGet("self", Name = RouteNames.GetMyAnnouncementDismissal)]
    [EndpointSummary("Get private announcement dismissal state for the current authenticated human")]
    [ProducesResponseType(typeof(HalResource<AnnouncementSelfStateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementSelfStateDto>>> GetSelf(
        CancellationToken cancellationToken = default)
    {
        var state = await selfQuery.QueryAsync(new GetMyAnnouncementDismissalQuery(), cancellationToken);
        return Ok(await selfAssembler.ToResource(
            AnnouncementTransportMapper.ToSelfStateDto(state.TenantId, state.Current, state.Marker), HttpContext));
    }

    [HttpPut("instance", Name = RouteNames.UpdateInstanceAnnouncement)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Apply a typed instance announcement publication change")]
    [ProducesResponseType(typeof(HalResource<AnnouncementPublicationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementPublicationReceiptDto>>> UpdateInstance(
        [FromBody] AnnouncementUpdateDto input, CancellationToken cancellationToken = default)
    {
        var update = MapUpdate(input, AnnouncementOwner.Instance);
        var actor = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (actor is null)
            return this.ToAuthenticationRequiredProblem();
        return await ToPublicationResponse(await updateInstance.ExecuteAsync(
            new UpdateInstanceAnnouncementCommand(actor.Value, update), cancellationToken), cancellationToken);
    }

    [HttpPut("tenant", Name = RouteNames.UpdateTenantAnnouncement)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Apply a typed publication change to the current tenant's announcement")]
    [ProducesResponseType(typeof(HalResource<AnnouncementPublicationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementPublicationReceiptDto>>> UpdateTenant(
        [FromBody] AnnouncementUpdateDto input, CancellationToken cancellationToken = default)
    {
        Guid tenantId = tenantContext.TenantId;
        var update = MapUpdate(input, AnnouncementOwner.ForTenant(tenantId));
        var actor = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (actor is null)
            return this.ToAuthenticationRequiredProblem();
        return await ToPublicationResponse(await updateTenant.ExecuteAsync(
            new UpdateTenantAnnouncementCommand(tenantId, actor.Value, update), cancellationToken), cancellationToken);
    }

    [HttpPut("tenants/{tenantId:guid}", Name = RouteNames.UpdateManagedTenantAnnouncement)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Apply a typed instance-managed tenant announcement publication change")]
    [ProducesResponseType(typeof(HalResource<AnnouncementPublicationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementPublicationReceiptDto>>> UpdateManagedTenant(
        Guid tenantId, [FromBody] AnnouncementUpdateDto input, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new BadRequestException("An actual managed tenant identity is required.");
        var update = MapUpdate(input, AnnouncementOwner.ForTenant(tenantId));
        var actor = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (actor is null)
            return this.ToAuthenticationRequiredProblem();
        return await ToPublicationResponse(await updateManaged.ExecuteAsync(
            new UpdateManagedTenantAnnouncementCommand(tenantId, actor.Value, update), cancellationToken), cancellationToken);
    }

    [HttpPost("self/dismiss", Name = RouteNames.DismissAnnouncement)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Dismiss the observed announcement using its complete source witness and expected marker")]
    [ProducesResponseType(typeof(HalResource<AnnouncementDismissalReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<AnnouncementDismissalReceiptDto>>> Dismiss(
        [FromBody] AnnouncementDismissRequestDto input, CancellationToken cancellationToken = default)
    {
        (AnnouncementObservation Observed, AnnouncementDismissalMarker? ExpectedMarker) mapped;
        try
        {
            mapped = AnnouncementTransportMapper.ToNative(input, tenantContext.TenantId);
        }
        catch (ArgumentException)
        {
            throw new BadRequestException("The announcement dismissal input is invalid.");
        }
        var actor = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (actor is null)
            return this.ToAuthenticationRequiredProblem();
        var result = await dismiss.ExecuteAsync(
            new DismissAnnouncementCommand(actor.Value, mapped.Observed, mapped.ExpectedMarker), cancellationToken);
        if (result.Decision is AnnouncementDismissalDecision.SourceChanged or AnnouncementDismissalDecision.MarkerChanged
            or AnnouncementDismissalDecision.NotVisible)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Announcement dismissal changed",
                detail: "Refresh private announcement state before submitting another dismissal.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.Decision switch
                    {
                        AnnouncementDismissalDecision.SourceChanged => "announcement_source_changed",
                        AnnouncementDismissalDecision.MarkerChanged => "announcement_marker_changed",
                        _ => "announcement_not_visible"
                    }
                });
        if (result.Decision is not (AnnouncementDismissalDecision.Apply or AnnouncementDismissalDecision.AlreadyDismissed)
            || result.Marker is null)
            throw new InvalidOperationException("A settled announcement dismissal requires its marker.");
        return Ok(await dismissalAssembler.ToResource(new()
        {
            Decision = AnnouncementTransportMapper.ToDto(result.Decision),
            Marker = AnnouncementTransportMapper.ToDto(result.Marker)
        }, HttpContext));
    }

    private static AnnouncementUpdate MapUpdate(AnnouncementUpdateDto input, AnnouncementOwner owner)
    {
        try { return AnnouncementTransportMapper.ToNative(input, owner); }
        catch (ArgumentException) { throw new BadRequestException("The announcement publication input is invalid."); }
    }

    private async Task<ActionResult<HalResource<AnnouncementPublicationReceiptDto>>> ToPublicationResponse(
        AnnouncementPublicationResult result, CancellationToken cancellationToken)
    {
        if (result.Outcome == AnnouncementPublicationOutcome.SourceChanged)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Announcement source changed",
                detail: "Refresh the original announcement source before submitting another change.",
                extensions: new Dictionary<string, object?> { ["code"] = "announcement_source_changed" });
        if (result.Outcome == AnnouncementPublicationOutcome.Locked)
            return this.ToForbiddenProblem(detail: "The announcement source is locked.");
        var outcome = result.Outcome switch
        {
            AnnouncementPublicationOutcome.Applied => AnnouncementPublicationOutcomeDto.Applied,
            AnnouncementPublicationOutcome.Unchanged => AnnouncementPublicationOutcomeDto.Unchanged,
            _ => throw new InvalidOperationException("A successful announcement publication requires a settled outcome.")
        };
        if (result.Outcome == AnnouncementPublicationOutcome.Applied)
            await cacheStore.EvictByTagAsync("public-experience-shell", cancellationToken);
        return Ok(await publicationAssembler.ToResource(new()
        {
            Outcome = outcome,
            Source = AnnouncementTransportMapper.ToDto(result.Source)
        }, HttpContext));
    }
}
