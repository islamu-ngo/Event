namespace Explore.API.Controllers;

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[ApiVersion("0.1")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Route("api/admin/instance/configuration-manifest/export")]
[Tags("Instance Configuration")]
public sealed class ConfigurationManifestExportsController(
    IQueryHandler<ExportConfigurationManifestQuery, ConfigurationManifestExportResult> exporter)
    : ControllerBase
{
    [HttpGet("", Name = RouteNames.ExportConfigurationManifest)]
    [InstanceManagement]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
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
        "Downloads instance configuration as overrides or a flattened portable view. Include eligible tenants only with explicit combined scope.")]
    public async Task<FileContentResult> Export(
        [FromQuery] ConfigurationManifestExportView? view = null,
        [FromQuery] ConfigurationManifestExportScope? scope = null,
        CancellationToken cancellationToken = default)
    {
        ConfigurationManifestExportResult export = await exporter.QueryAsync(
            new ExportConfigurationManifestQuery(
                view ?? ConfigurationManifestExportView.Overrides,
                scope ?? ConfigurationManifestExportScope.Instance),
            cancellationToken);

        return File(
            export.Utf8Json.ToArray(),
            ConfigurationManifestExportApiContract.MediaType,
            export.FileName);
    }
}
