namespace Explore.API.Controllers;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateConfigurationDirectTransferRequest(
    [property: Required, MaxLength(200)] string SourceAuthority,
    [property: Required] Uri DestinationOrigin,
    [property: Required, RegularExpression("^[0-9a-f]{64}$")] string ArtifactDigest,
    [property: Range(1, 4 * 1024 * 1024)] int ArtifactByteLength);

[ApiController]
[ApiVersion("0.1")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Route("api/configuration-transfers/v1alpha2")]
[Tags("Configuration Transfer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
[RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
public sealed class ConfigurationDirectTransferController(
    ConfigurationDirectTransferService transfers,
    IAuthorizationProvider authorization)
    : ConfigurationImportSessionsControllerBase
{
    public const string NonceHeader = "X-Configuration-Transfer-Nonce";
    public const string DestinationProofHeader =
        "X-Configuration-Transfer-Destination-Proof";
    public const string ChunkOffsetHeader = "X-Configuration-Transfer-Offset";
    public const string ChunkDigestHeader = "X-Configuration-Transfer-Chunk-Digest";

    [HttpPost("instance", Name = RouteNames.CreateInstanceConfigurationTransfer)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferCreated), StatusCodes.Status201Created)]
    public Task<ActionResult<ConfigurationDirectTransferCreated>> CreateInstance(
        [FromBody] CreateConfigurationDirectTransferRequest request,
        CancellationToken cancellationToken) =>
        CreateAsync(ConfigurationImportTarget.ForInstance(), request, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}", Name = RouteNames.CreateTenantConfigurationTransfer)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferCreated), StatusCodes.Status201Created)]
    public Task<ActionResult<ConfigurationDirectTransferCreated>> CreateTenant(
        Guid tenantId,
        [FromBody] CreateConfigurationDirectTransferRequest request,
        CancellationToken cancellationToken) =>
        CreateAsync(ConfigurationImportTarget.ForTenant(tenantId), request, cancellationToken);

    [HttpPost("instance/{sessionId:guid}/source-approval", Name = RouteNames.ApproveInstanceConfigurationTransferSource)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> ApproveInstanceSource(
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        ApproveSourceAsync(
            ConfigurationImportTarget.ForInstance(),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/{sessionId:guid}/source-approval", Name = RouteNames.ApproveTenantConfigurationTransferSource)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> ApproveTenantSource(
        Guid tenantId,
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        ApproveSourceAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    [HttpPut("instance/{sessionId:guid}/chunks", Name = RouteNames.AppendInstanceConfigurationTransferChunk)]
    [Consumes("application/octet-stream")]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> AppendInstance(
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        [FromHeader(Name = ChunkOffsetHeader)] int offset,
        [FromHeader(Name = ChunkDigestHeader), Required] string digest,
        CancellationToken cancellationToken) =>
        AppendAsync(
            ConfigurationImportTarget.ForInstance(),
            sessionId,
            nonce,
            proof,
            offset,
            digest,
            cancellationToken);

    [HttpPut("tenants/{tenantId:guid}/{sessionId:guid}/chunks", Name = RouteNames.AppendTenantConfigurationTransferChunk)]
    [Consumes("application/octet-stream")]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> AppendTenant(
        Guid tenantId,
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        [FromHeader(Name = ChunkOffsetHeader)] int offset,
        [FromHeader(Name = ChunkDigestHeader), Required] string digest,
        CancellationToken cancellationToken) =>
        AppendAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            sessionId,
            nonce,
            proof,
            offset,
            digest,
            cancellationToken);

    [HttpPost("instance/{sessionId:guid}/complete", Name = RouteNames.CompleteInstanceConfigurationTransfer)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> CompleteInstance(
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            ConfigurationImportTarget.ForInstance(),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/{sessionId:guid}/complete", Name = RouteNames.CompleteTenantConfigurationTransfer)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> CompleteTenant(
        Guid tenantId,
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    [HttpPost("instance/{sessionId:guid}/promote", Name = RouteNames.PromoteInstanceConfigurationTransfer)]
    [ProducesResponseType(typeof(HalResource<ConfigurationImportSessionCreatedResult>), StatusCodes.Status201Created)]
    public Task<ActionResult<HalResource<ConfigurationImportSessionCreatedResult>>> PromoteInstance(
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        PromoteAsync(
            ConfigurationImportTarget.ForInstance(),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/{sessionId:guid}/promote", Name = RouteNames.PromoteTenantConfigurationTransfer)]
    [ProducesResponseType(typeof(HalResource<ConfigurationImportSessionCreatedResult>), StatusCodes.Status201Created)]
    public Task<ActionResult<HalResource<ConfigurationImportSessionCreatedResult>>> PromoteTenant(
        Guid tenantId,
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        PromoteAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    [HttpDelete("instance/{sessionId:guid}", Name = RouteNames.CancelInstanceConfigurationTransfer)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> CancelInstance(
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        CancelAsync(
            ConfigurationImportTarget.ForInstance(),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    [HttpDelete("tenants/{tenantId:guid}/{sessionId:guid}", Name = RouteNames.CancelTenantConfigurationTransfer)]
    [ProducesResponseType(typeof(ConfigurationDirectTransferProgress), StatusCodes.Status200OK)]
    public Task<ActionResult<ConfigurationDirectTransferProgress>> CancelTenant(
        Guid tenantId,
        Guid sessionId,
        [FromHeader(Name = NonceHeader), Required] string nonce,
        [FromHeader(Name = DestinationProofHeader), Required] string proof,
        CancellationToken cancellationToken) =>
        CancelAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            sessionId,
            nonce,
            proof,
            cancellationToken);

    private async Task<ActionResult<ConfigurationDirectTransferCreated>> CreateAsync(
        ConfigurationImportTarget target,
        CreateConfigurationDirectTransferRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(target, cancellationToken))
            return Forbidden();
        ConfigurationDirectTransferCreated created = await transfers.CreateAsync(
            target,
            request.SourceAuthority,
            request.DestinationOrigin,
            request.ArtifactDigest,
            request.ArtifactByteLength,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    private async Task<ActionResult<ConfigurationDirectTransferProgress>> ApproveSourceAsync(
        ConfigurationImportTarget target,
        Guid sessionId,
        string nonce,
        string proof,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(target, cancellationToken))
            return Forbidden();
        return Ok(await transfers.ApproveSourceAsync(
            sessionId,
            target,
            nonce,
            proof,
            cancellationToken));
    }

    private async Task<ActionResult<ConfigurationDirectTransferProgress>> AppendAsync(
        ConfigurationImportTarget target,
        Guid sessionId,
        string nonce,
        string proof,
        int offset,
        string digest,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(target, cancellationToken))
            return Forbidden();
        if (Request.ContentLength is > ConfigurationDirectTransferService.MaximumChunkBytes)
            return BadRequest();
        ReadOnlyMemory<byte> bytes = await ReadArtifactAsync(Request, cancellationToken);
        return Ok(await transfers.AppendAsync(
            sessionId,
            target,
            nonce,
            proof,
            offset,
            bytes,
            digest,
            cancellationToken));
    }

    private async Task<ActionResult<ConfigurationDirectTransferProgress>> CompleteAsync(
        ConfigurationImportTarget target,
        Guid sessionId,
        string nonce,
        string proof,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(target, cancellationToken))
            return Forbidden();
        return Ok(await transfers.CompleteAsync(
            sessionId,
            target,
            nonce,
            proof,
            cancellationToken));
    }

    private async Task<ActionResult<HalResource<ConfigurationImportSessionCreatedResult>>> PromoteAsync(
        ConfigurationImportTarget target,
        Guid sessionId,
        string nonce,
        string proof,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(target, cancellationToken))
            return Forbidden();
        ConfigurationImportSessionCreatedResult created = await transfers.PromoteAsync(
            sessionId,
            target,
            nonce,
            proof,
            cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            WithSessionLinks(created, created.SessionId, target.TenantId));
    }

    private async Task<ActionResult<ConfigurationDirectTransferProgress>> CancelAsync(
        ConfigurationImportTarget target,
        Guid sessionId,
        string nonce,
        string proof,
        CancellationToken cancellationToken)
    {
        if (!await CanUpdateAsync(target, cancellationToken))
            return Forbidden();
        return Ok(await transfers.CancelAsync(
            sessionId,
            target,
            nonce,
            proof,
            cancellationToken));
    }

    private async Task<bool> CanUpdateAsync(
        ConfigurationImportTarget target,
        CancellationToken cancellationToken)
    {
        bool tenant = target.TenantId.HasValue;
        AuthorizationDecision decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                tenant ? ResourceKinds.TenantSetting : ResourceKinds.InstanceSetting,
                tenant
                    ? CreateTenantConfigurationImportSessionCommand.ResourceKey
                    : CreateInstanceConfigurationImportSessionCommand.ResourceKey,
                tenant
                    ? AuthorizationActions.TenantSettings.Update
                    : AuthorizationActions.InstanceSettings.Update,
                tenant
                    ? new AuthorizationScope(TenantId: target.TenantId!.Value.ToString("D"))
                    : AuthorizationScope.Empty,
                tenant
                    ? new TenantSettingAuthorizationFacts(
                        target.TenantId!.Value,
                        CreateTenantConfigurationImportSessionCommand.ResourceKey)
                    : InstanceScopedAuthorizationFacts.Instance,
                new AuthorizationSubject(RequiredUserId)),
            cancellationToken);
        return decision.IsAllowed;
    }

    private ObjectResult Forbidden() => Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Configuration transfer unavailable",
        detail: "The current target authority does not permit this transfer operation.");
}
