using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/admin/custom-property-definitions")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public class CustomPropertyGovernanceController(IMediator mediator) : ControllerBase
{

    /// <summary>
    /// Get governance report listing all active Layer 3 definitions with promotion recommendations.
    /// </summary>
    [HttpGet("governance-report", Name = RouteNames.GetCustomPropertyGovernanceReport)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(PaginatedResult<CustomPropertyGovernanceRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedResult<CustomPropertyGovernanceRowDto>>> GetGovernanceReport(
        [FromQuery] CustomPropertyGovernanceReportQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetCustomPropertyGovernanceReportQuery
            {
                TenantId = query.TenantId,
                Filter = new GovernanceReportFilterDto
                {
                    EntityScope = query.Scope,
                    Recommendation = query.Recommendation,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize
                }
            },
            cancellationToken);

        return Ok(result);
    }
}
