namespace Explore.API.Controllers;

using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ConfigurationImport;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[ApiVersion("0.1")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Route("api/control-plane/configuration-import/sessions")]
[Tags("Control Plane Configuration")]
public sealed class InstanceConfigurationImportSessionsController(
    IMediator mediator,
    IAuthorizationProvider authorization)
    : ConfigurationImportSessionsControllerBase
{
    [HttpPost("", Name = RouteNames.CreateInstanceConfigurationImportSession)]
    [Consumes(ConfigurationManifestContractMetadata.MediaType)]
    [PrivateNoStore]
    [EnableRateLimiting(
        ConfigurationImportApiBoundary.UploadRateLimitPolicy)]
    [RequestTimeout(
        ConfigurationImportApiBoundary.UploadRequestTimeoutPolicy)]
    [RequestSizeLimit(
        ConfigurationImportApiBoundary.MaximumUploadBytes)]
    [ProducesResponseType(
        typeof(HalResource<ConfigurationImportSessionCreatedResult>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<HalResource<ConfigurationImportSessionCreatedResult>>>
        Create(
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> bytes = await ReadArtifactAsync(
            Request,
            cancellationToken);
        ConfigurationImportSessionCreatedResult created =
            await mediator.Send(
                new CreateInstanceConfigurationImportSessionCommand(bytes),
                cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            WithSessionLinks(
                created,
                created.SessionId,
                tenantId: null));
    }

    [HttpPost(
        "{sessionId:guid}/preview",
        Name = RouteNames.PreviewInstanceConfigurationImportSession)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [ProducesResponseType(
        typeof(HalResource<ConfigurationImportPreviewResult>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<HalResource<ConfigurationImportPreviewResult>>> Preview(
        Guid sessionId,
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)]
        [Required]
        string accessToken,
        [FromBody] ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        PreviewCore(sessionId, accessToken, request, cancellationToken);

    [HttpPost(
        "{sessionId:guid}/refresh",
        Name = RouteNames.RefreshInstanceConfigurationImportSession)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [ProducesResponseType(
        typeof(HalResource<ConfigurationImportPreviewResult>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public Task<ActionResult<HalResource<ConfigurationImportPreviewResult>>> Refresh(
        Guid sessionId,
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)]
        [Required]
        string accessToken,
        [FromBody] ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        PreviewCore(sessionId, accessToken, request, cancellationToken);

    [HttpPost(
        "{sessionId:guid}/apply",
        Name = RouteNames.ApplyInstanceConfigurationImportSession)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [ProducesResponseType(
        typeof(HalResource<ConfigurationImportOperationResult>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HalResource<ConfigurationImportOperationResult>>>
        Apply(
        Guid sessionId,
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)]
        [Required]
        string accessToken,
        [FromBody] ConfigurationImportApplyRequest request,
        CancellationToken cancellationToken)
    {
        ConfigurationImportOperationResult result = await mediator.Send(
            new ApplyInstanceConfigurationImportCommand(
                sessionId,
                accessToken,
                request.Preview,
                request.RollbackOfOperationId,
                request.ManagedScheduleId),
            cancellationToken);
        return Ok(WithOperationLinks(result, tenantId: null, canRollback: true));
    }

    [HttpGet(
        "operations",
        Name = RouteNames.ListInstanceConfigurationImportHistory)]
    [PrivateNoStore]
    [ProducesResponseType(
        typeof(HalResource<ConfigurationImportHistoryResult>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<ConfigurationImportHistoryResult>>>
        History(
        [FromQuery] int maximumCount = 50,
        CancellationToken cancellationToken = default)
    {
        var operations = await mediator.Send(
            new ListInstanceConfigurationImportHistoryQuery(maximumCount),
            cancellationToken);
        return Ok(new HalResource<ConfigurationImportHistoryResult>(
            new ConfigurationImportHistoryResult(operations)));
    }

    [HttpGet(
        "operations/{operationId:guid}",
        Name = RouteNames.GetInstanceConfigurationImportReceipt)]
    [PrivateNoStore]
    [ProducesResponseType(
        typeof(HalResource<ConfigurationImportOperationResult>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<ConfigurationImportOperationResult>>>
        Receipt(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ConfigurationImportOperationResult result = await mediator.Send(
            new GetInstanceConfigurationImportReceiptQuery(operationId),
            cancellationToken);
        return Ok(WithOperationLinks(
            result,
            tenantId: null,
            await CanUpdateAsync(
                authorization,
                tenantId: null,
                cancellationToken)));
    }

    [HttpPost(
        "operations/{operationId:guid}/rollback",
        Name = RouteNames.CreateInstanceConfigurationRollbackSession)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [ProducesResponseType(
        typeof(HalResource<ConfigurationImportRollbackSessionCreatedResult>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<ConfigurationImportRollbackSessionCreatedResult>>>
        CreateRollback(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ConfigurationImportRollbackSessionCreatedResult result =
            await mediator.Send(
                new CreateInstanceConfigurationRollbackSessionCommand(operationId),
                cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            WithSessionLinks(result, result.Session.SessionId, tenantId: null));
    }

    [HttpDelete(
        "{sessionId:guid}",
        Name = RouteNames.CancelInstanceConfigurationImportSession)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Cancel(
        Guid sessionId,
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)]
        [Required]
        string accessToken,
        CancellationToken cancellationToken)
    {
        await mediator.Send(
            new CancelInstanceConfigurationImportSessionCommand(
                sessionId,
                accessToken),
            cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult<HalResource<ConfigurationImportPreviewResult>>>
        PreviewCore(
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ConfigurationImportPreviewResult preview = await mediator.Send(
            new PreviewInstanceConfigurationImportSessionCommand(
                sessionId,
                accessToken,
                request),
            cancellationToken);
        return Ok(WithSessionLinks(
            preview,
            sessionId,
            tenantId: null));
    }
}
