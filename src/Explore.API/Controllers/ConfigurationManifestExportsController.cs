namespace Explore.API.Controllers;

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[ApiVersion("0.1")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Route("api/control-plane/configuration-manifest/export")]
[Tags("Control Plane Configuration")]
public sealed class ConfigurationManifestExportsController(IMediator mediator)
    : ControllerBase
{
    [HttpGet("", Name = RouteNames.ExportConfigurationManifest)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [Produces(ConfigurationManifestExportApiContract.MediaType)]
    [ProducesResponseType<FileContentResult>(
        StatusCodes.Status200OK,
        ConfigurationManifestExportApiContract.MediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EndpointSummary("Export configuration manifest")]
    [EndpointDescription(
        "Downloads the current instance and all active tenants as explicit overrides or a flattened portable view.")]
    public async Task<FileContentResult> Export(
        [FromQuery] ConfigurationManifestExportView? view = null,
        CancellationToken cancellationToken = default)
    {
        ConfigurationManifestExportResult export = await mediator.Send(
            new ExportConfigurationManifestQuery(
                view ?? ConfigurationManifestExportView.Overrides),
            cancellationToken);

        return File(
            export.Utf8Json.ToArray(),
            ConfigurationManifestExportApiContract.MediaType,
            export.FileName);
    }
}
