namespace Explore.API.Controllers;

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
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
[Route("api/tenants/{tenantId:guid}/configuration-package/export")]
[Tags("Tenant Configuration")]
public sealed class TenantConfigurationPackagesController(IMediator mediator)
    : ControllerBase
{
    [HttpGet("", Name = RouteNames.ExportTenantConfigurationPackage)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [Produces(TenantConfigurationPackageContractMetadata.MediaType)]
    [ProducesResponseType<FileContentResult>(
        StatusCodes.Status200OK,
        TenantConfigurationPackageContractMetadata.MediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    public async Task<FileContentResult> Export(
        Guid tenantId,
        [FromQuery] ConfigurationManifestExportView? view = null,
        CancellationToken cancellationToken = default)
    {
        TenantConfigurationPackageExportResult export = await mediator.Send(
            new ExportTenantConfigurationPackageQuery(
                tenantId,
                view ?? ConfigurationManifestExportView.Overrides),
            cancellationToken);
        return File(
            export.Utf8Json.ToArray(),
            TenantConfigurationPackageContractMetadata.MediaType,
            export.FileName);
    }
}
