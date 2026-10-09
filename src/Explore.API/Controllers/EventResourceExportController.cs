using System.Collections.Immutable;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/event/{eventId:guid}/resources/export")]
[Tags("EventResourceExport")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class EventResourceExportController(
    IQueryHandler<ExportEventResourceMetadataQuery, EventResourceManagementReadResult<EventResourceMetadataExportPageDto>> export)
    : ControllerBase
{
    [HttpGet(Name = RouteNames.ExportEventResourceMetadata)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventResourceMetadataExportPageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResourceMetadataExportPageDto>> Get(
        Guid eventId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await export.QueryAsync(new(eventId, page, pageSize), cancellationToken);
        if (result.Projection is { } projection)
        {
            return new EventResourceProjectionResult<EventResourceMetadataExportPageDto>(projection, (value, _) =>
            {
                var items = value.Items.Select(item => item.DownloadAuthorized
                    && Url.RouteUrl(RouteNames.GetEventResourceContent, new { id = item.Id }) is { } href
                        ? item with { Download = new(href) }
                        : item with { Download = null }).ToImmutableArray();
                return Task.FromResult<ActionResult>(Ok(value with { Items = items }));
            }, ReadFailure);
        }
        return ReadFailure(result.Outcome);
    }

    private ActionResult ReadFailure(EventResourceAuthorityOutcome outcome) => outcome switch
        {
            EventResourceAuthorityOutcome.NotFound => this.ToNotFoundProblem(
                new("Resource not found", "The requested resource was not found.")),
            EventResourceAuthorityOutcome.AuthenticationRequired => this.ToAuthenticationRequiredProblem(),
            EventResourceAuthorityOutcome.Forbidden => this.ToForbiddenProblem(),
            _ => this.ToServiceUnavailableProblem("Resource authority unavailable",
                "Current resource authority could not be established.", EventResourceManagementFailureCodes.Unavailable)
        };
}
