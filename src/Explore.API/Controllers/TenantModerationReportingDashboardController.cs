using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenant/settings/moderation-reporting/dashboard")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class TenantModerationReportingDashboardController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<TenantModerationReportingDashboardDto, TenantModerationReportingDashboardDto> resourceAssembler)
    : EventControllerBase
{
    [HttpGet(Name = RouteNames.GetTenantModerationReportingDashboard)]
    [EndpointSummary("Get Tenant Moderation Reporting Dashboard")]
    [EndpointDescription("Returns current-tenant moderation reporting queue and provider sync health without report payloads or provider secrets.")]
    [ProducesResponseType(typeof(HalResource<TenantModerationReportingDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<TenantModerationReportingDashboardDto>>> GetDashboard(
        CancellationToken cancellationToken = default)
    {
        var dashboard = await mediator.Send(
            new GetTenantModerationReportingDashboardRequest(tenantContext.TenantId),
            cancellationToken);

        var resource = await resourceAssembler.ToResource(dashboard, HttpContext);
        return Ok(resource);
    }
}
