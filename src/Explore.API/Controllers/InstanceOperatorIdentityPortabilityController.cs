namespace Explore.API.Controllers;

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiVersion("0.1")]
[Route("api/instance-operator-identity")]
[ApiController]
[Authorize]
[PrivateNoStore]
[EndpointClassification(EndpointClass.Admin)]
public sealed class InstanceOperatorIdentityPortabilityController(
    IQueryHandler<ExportInstanceOperatorIdentityQuery, BaseCommandResponse<OperatorIdentityManifest>> export,
    ICommandHandler<ImportInstanceOperatorIdentityCommand, BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> import)
    : EventControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("operatorIdentityManifest", "Operator identity portability failed",
            "The operator identity manifest was not accepted."))
        .Forbidden("Instance administrator required", "Instance administrator authority is required.",
            FailureCodes.AdminRequired)
        .Conflict("Operator identity revision conflict", "Export the current target revision before importing again.",
            FailureCodes.ConcurrencyConflict);

    [InstanceManagement]
    [HttpGet("manifest", Name = RouteNames.ExportInstanceOperatorIdentity)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(OperatorIdentityManifest), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<OperatorIdentityManifest> result =
            await export.QueryAsync(new(), cancellationToken);
        return result.IsSuccess
            ? File(OperatorIdentityManifestJson.Serialize(result.Id!), "application/json", "operator-identity.json")
            : Failures.Map(this, result);
    }

    [InstanceManagement]
    [HttpPost("manifest/import", Name = RouteNames.ImportInstanceOperatorIdentity)]
    [SuppressIdempotencyResponseStorage]
    [Consumes("application/json")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(OperatorIdentityManifestJson.MaximumBytes + 1024)]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceOperatorIdentitySavedDocument>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Import(
        [FromBody] ImportInstanceOperatorIdentityCommand request,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> result =
            await import.ExecuteAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result) : Failures.Map(this, result);
    }
}
