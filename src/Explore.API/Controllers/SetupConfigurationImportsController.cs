namespace Explore.API.Controllers;

using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ConfigurationImport;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.SetupLive;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Hateoas;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using ISLAMU.Wire.Contracts.SetupLive;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[ApiVersion("0.1")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[Route("api/tenants/{tenantId:guid}/setup/enrollments/{enrollmentId:guid}/configuration-import/sessions")]
[Tags("Setup Live")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
public sealed class SetupConfigurationImportsController(
    SetupLiveApplicationService enrollment,
    SetupConfigurationImportApplicationService imports,
    IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery)
    : ConfigurationImportSessionsControllerBase
{
    [HttpPost("", Name = RouteNames.CreateSetupConfigurationImportSession)]
    [SuppressIdempotencyResponseStorage]
    [Consumes(TenantConfigurationPackageContractMetadata.MediaType)]
    [EnableRateLimiting(ConfigurationImportApiBoundary.UploadRateLimitPolicy)]
    [RequestTimeout(ConfigurationImportApiBoundary.UploadRequestTimeoutPolicy)]
    [RequestSizeLimit(ConfigurationImportApiBoundary.MaximumUploadBytes)]
    [ProducesResponseType(typeof(HalResource<ConfigurationImportSessionCreatedResult>),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> CreateSetupConfigurationImportSession(
        Guid tenantId, Guid enrollmentId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        CancellationToken cancellationToken)
    {
        Guid? userId = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return this.ToAuthenticationRequiredProblem();
        if (!await enrollment.ValidateConfigurationImportAsync(
                tenantId, enrollmentId, userId.Value, capability, cancellationToken))
            throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ArtifactMissing);

        ReadOnlyMemory<byte> artifact = await ReadArtifactAsync(Request, cancellationToken);
        ConfigurationImportSessionCreatedResult result = await imports.CreateAsync(
            tenantId, enrollmentId, userId.Value, capability, artifact, cancellationToken);
        return Hal(SessionLinks(result, tenantId, enrollmentId, result.SessionId),
            StatusCodes.Status201Created);
    }

    [HttpPost("{sessionId:guid}/preview", Name = RouteNames.PreviewSetupConfigurationImportSession)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [ProducesResponseType(typeof(HalResource<ConfigurationImportPreviewResult>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewSetupConfigurationImportSession(
        Guid tenantId, Guid enrollmentId, Guid sessionId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)][Required] string accessToken,
        [FromBody] ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        Guid? userId = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return this.ToAuthenticationRequiredProblem();
        ConfigurationImportPreviewResult result = await imports.PreviewAsync(
            tenantId, enrollmentId, userId.Value, capability,
            sessionId, accessToken, request, cancellationToken);
        return Hal(SessionLinks(result, tenantId, enrollmentId, sessionId),
            StatusCodes.Status200OK);
    }

    [HttpPost("{sessionId:guid}/apply", Name = RouteNames.ApplySetupConfigurationImportSession)]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [ProducesResponseType(typeof(HalResource<ConfigurationImportOperationResult>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> ApplySetupConfigurationImportSession(
        Guid tenantId, Guid enrollmentId, Guid sessionId,
        [FromHeader(Name = SetupLiveContractMetadata.CapabilityHeader)] string? capability,
        [FromHeader(Name = ConfigurationImportApiBoundary.AccessTokenHeader)][Required] string accessToken,
        [FromBody] ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        Guid? userId = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (userId is null)
            return this.ToAuthenticationRequiredProblem();
        ConfigurationImportOperationResult result = await imports.ApplyAsync(
            tenantId, enrollmentId, userId.Value, capability,
            sessionId, accessToken, request, cancellationToken);
        return Hal(new HalResource<ConfigurationImportOperationResult>(result),
            StatusCodes.Status200OK);
    }

    private HalResource<T> SessionLinks<T>(
        T result, Guid tenantId, Guid enrollmentId, Guid sessionId) where T : class
    {
        var values = new { tenantId, enrollmentId, sessionId };
        var resource = new HalResource<T>(result).WithLink(
            LinkRelations.PreviewConfigurationImport,
            HalLink.CreateAction(
                Url.Link(RouteNames.PreviewSetupConfigurationImportSession, values)
                ?? throw new InvalidOperationException("Setup import preview route is unavailable."),
                HttpMethods.Post));
        if (result is ConfigurationImportPreviewResult { IsApplyReady: true })
            resource.WithLink(
                LinkRelations.ApplyConfigurationImport,
                HalLink.CreateAction(
                    Url.Link(RouteNames.ApplySetupConfigurationImportSession, values)
                    ?? throw new InvalidOperationException("Setup import apply route is unavailable."),
                    HttpMethods.Post));
        return resource;
    }

    private static ObjectResult Hal(object value, int statusCode) => new(value)
    {
        StatusCode = statusCode,
        ContentTypes = { SetupLiveContractMetadata.SuccessMediaType }
    };
}
