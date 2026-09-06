// ABOUTME: Hosts the authenticated server-private endpoint that exchanges a verified PDS session for a platform JWT.
// ABOUTME: Excludes credential-bearing contracts from OpenAPI and disables response caching.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/auth/atproto")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AtprotoSessionController(
    IMediator mediator,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpPost("session", Name = RouteNames.BootstrapAtprotoSession)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.AtprotoBootstrap)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(160 * 1024)]
    [EndpointSummary("Bootstrap ATProto platform session")]
    [EndpointDescription("Server-private BFF bridge; excluded from public API discovery and generated clients.")]
    [ProducesResponseType<BffAtprotoSessionBridgeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<BffAtprotoSessionBridgeResponse>> BootstrapSession(
        [FromBody] BffAtprotoSessionBridgeRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryParseClassification(request.Classification, out var classification))
        {
            return ProblemResponse(StatusCodes.Status400BadRequest, "Invalid ATProto subject classification");
        }

        if (!TryGetCanonicalActorTarget(
                request.CanonicalActorId,
                request.ExpectedCanonicalActorConcurrencyStamp,
                out var canonicalActorId,
                out var expectedCanonicalActorConcurrencyStamp))
        {
            return ProblemResponse(StatusCodes.Status400BadRequest, "Invalid ATProto canonical Actor target");
        }

        if (!TryGetCanonicalActorTarget(
                User.FindAll(AtprotoJwtOptions.CanonicalActorIdClaim).Select(claim => claim.Value).ToArray(),
                User.FindAll(AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim).Select(claim => claim.Value).ToArray(),
                out var claimedCanonicalActorId,
                out var claimedExpectedCanonicalActorConcurrencyStamp)
            || canonicalActorId != claimedCanonicalActorId
            || expectedCanonicalActorConcurrencyStamp != claimedExpectedCanonicalActorConcurrencyStamp)
        {
            return ProblemResponse(StatusCodes.Status401Unauthorized, "ATProto bootstrap target binding mismatch");
        }

        if (!string.Equals(
                User.FindFirstValue(AtprotoJwtOptions.DidClaim),
                request.ExpectedDid,
                StringComparison.Ordinal)
            || !string.Equals(
                User.FindFirstValue(AtprotoJwtOptions.ClassificationClaim),
                request.Classification,
                StringComparison.Ordinal))
        {
            return ProblemResponse(StatusCodes.Status401Unauthorized, "ATProto bootstrap binding mismatch");
        }
        if (!AtprotoDid.TryParse(request.ExpectedDid, out AtprotoDid expectedDid))
        {
            return ProblemResponse(StatusCodes.Status400BadRequest, "Invalid ATProto session request");
        }

        var sessionPayload = JsonSerializer.SerializeToUtf8Bytes(request.OAuthSession);
        var result = await mediator.Send(new BootstrapAtprotoSessionCommand(
            expectedDid,
            request.ExpectedPdsUri,
            request.OAuthClientKeyId,
            classification,
            sessionPayload,
            canonicalActorId,
            expectedCanonicalActorConcurrencyStamp), cancellationToken);

        if (result.Success
            && result.UserId is { } userId
            && result.ActorId is { } actorId
            && result.ParticipationId is { } participationId
            && result.Classification is { } resultClassification
            && result.Token is { } token
            && result.ExpiresAt is { } expiresAt
            && result.CanonicalActorId == canonicalActorId
            && result.ExpectedCanonicalActorConcurrencyStamp == expectedCanonicalActorConcurrencyStamp)
        {
            return Ok(new BffAtprotoSessionBridgeResponse(
                userId,
                actorId,
                participationId,
                request.ExpectedDid,
                ToContractValue(resultClassification),
                token,
                expiresAt,
                canonicalActorId,
                expectedCanonicalActorConcurrencyStamp));
        }

        return result.FailureCode switch
        {
            "invalid_request" => ProblemResponse(StatusCodes.Status400BadRequest, "Invalid ATProto session request"),
            "account_not_linked" => ProblemResponse(StatusCodes.Status403Forbidden, "ATProto account is not linked"),
            "linked_identity_incomplete" or "identity_conflict" or "classification_conflict" =>
                ProblemResponse(StatusCodes.Status409Conflict, "ATProto identity conflict"),
            "invalid_session" or "session_binding_mismatch" or "pds_identity_mismatch" =>
                ProblemResponse(StatusCodes.Status401Unauthorized, "ATProto session verification failed"),
            _ => ProblemResponse(StatusCodes.Status502BadGateway, "ATProto provider verification failed")
        };
    }

    [HttpGet("session/current", Name = RouteNames.GetCurrentAtprotoSession)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.AtprotoSession)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [EndpointSummary("Get current ATProto OAuth session")]
    [EndpointDescription("Server-private BFF operation; requires the session bearer and a single-use bridge assertion.")]
    [ProducesResponseType<AtprotoCurrentSessionBridgeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AtprotoCurrentSessionBridgeResponse>> GetCurrentSession(
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (GetCurrentIdentity() is not { } identity)
        {
            return ProblemResponse(StatusCodes.Status401Unauthorized, "ATProto session authentication failed");
        }

        var session = await mediator.Send(
            new GetCurrentAtprotoOAuthSessionQuery(identity),
            cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return ProblemResponse(StatusCodes.Status404NotFound, "ATProto session not found");
        }

        using var document = JsonDocument.Parse(session.OAuthSessionPayload);
        return Ok(new AtprotoCurrentSessionBridgeResponse(
            session.Did.Value,
            session.ExpectedPdsUri.AbsoluteUri,
            session.OAuthClientKeyId,
            document.RootElement.Clone()));
    }

    [HttpPost("session/current", Name = RouteNames.RefreshCurrentAtprotoSession)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.AtprotoSession)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Refresh current ATProto OAuth session")]
    [EndpointDescription("Server-private BFF operation; persists rotated OAuth state before returning a replacement session token.")]
    [ProducesResponseType<BffAtprotoSessionRefreshResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BffAtprotoSessionRefreshResponse>> RefreshCurrentSession(
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (GetCurrentIdentity() is not { } identity)
        {
            return ProblemResponse(StatusCodes.Status401Unauthorized, "ATProto session authentication failed");
        }

        var result = await mediator.Send(
            new RefreshAtprotoSessionCommand(identity),
            cancellationToken).ConfigureAwait(false);
        return result.Success && result.Token is { } token && result.ExpiresAt is { } expiresAt
            ? Ok(new BffAtprotoSessionRefreshResponse(identity.UserId, identity.Did.Value, token, expiresAt))
            : ProblemResponse(StatusCodes.Status401Unauthorized, "ATProto reauthentication required");
    }

    [HttpDelete("session/current", Name = RouteNames.DeleteCurrentAtprotoSession)]
    [Authorize(AuthenticationSchemes = ApiAuthenticationSchemeNames.AtprotoSession)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [EndpointSummary("Delete current ATProto OAuth session")]
    [EndpointDescription("Server-private idempotent BFF operation; requires the session bearer and a single-use bridge assertion.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteCurrentSession(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (GetCurrentIdentity() is not { } identity)
        {
            return ProblemResponse(StatusCodes.Status401Unauthorized, "ATProto session authentication failed");
        }

        await mediator.Send(
            new RevokeAtprotoSessionCommand(identity),
            cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private AtprotoCurrentSessionIdentity? GetCurrentIdentity()
    {
        return Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
               && AtprotoDid.TryParse(User.FindFirstValue(AtprotoJwtOptions.DidClaim), out AtprotoDid did)
            ? new AtprotoCurrentSessionIdentity(tenantContext.TenantId, userId, did)
            : null;
    }

    private static bool TryParseClassification(
        string value,
        out AtprotoSubjectClassification classification)
    {
        classification = value switch
        {
            "person" => AtprotoSubjectClassification.Person,
            "organization" => AtprotoSubjectClassification.Organization,
            "group" => AtprotoSubjectClassification.Group,
            _ => default
        };
        return classification != default;
    }

    private static string ToContractValue(AtprotoSubjectClassification classification) => classification switch
    {
        AtprotoSubjectClassification.Person => "person",
        AtprotoSubjectClassification.Organization => "organization",
        AtprotoSubjectClassification.Group => "group",
        _ => throw new ArgumentOutOfRangeException(nameof(classification))
    };

    private static bool TryGetCanonicalActorTarget(
        Guid? canonicalActorId,
        Guid? expectedCanonicalActorConcurrencyStamp,
        out Guid? parsedCanonicalActorId,
        out Guid? parsedExpectedCanonicalActorConcurrencyStamp)
    {
        parsedCanonicalActorId = canonicalActorId;
        parsedExpectedCanonicalActorConcurrencyStamp = expectedCanonicalActorConcurrencyStamp;
        return canonicalActorId.HasValue == expectedCanonicalActorConcurrencyStamp.HasValue
               && canonicalActorId != Guid.Empty
               && expectedCanonicalActorConcurrencyStamp != Guid.Empty;
    }

    private static bool TryGetCanonicalActorTarget(
        string[] canonicalActorIdClaims,
        string[] expectedCanonicalActorConcurrencyStampClaims,
        out Guid? canonicalActorId,
        out Guid? expectedCanonicalActorConcurrencyStamp)
    {
        canonicalActorId = null;
        expectedCanonicalActorConcurrencyStamp = null;
        if (canonicalActorIdClaims.Length != expectedCanonicalActorConcurrencyStampClaims.Length
            || canonicalActorIdClaims.Length > 1)
        {
            return false;
        }

        if (canonicalActorIdClaims.Length == 0)
        {
            return true;
        }

        if (!Guid.TryParseExact(canonicalActorIdClaims[0], "D", out var parsedActorId)
            || parsedActorId == Guid.Empty
            || !Guid.TryParseExact(expectedCanonicalActorConcurrencyStampClaims[0], "D", out var parsedConcurrencyStamp)
            || parsedConcurrencyStamp == Guid.Empty)
        {
            return false;
        }

        canonicalActorId = parsedActorId;
        expectedCanonicalActorConcurrencyStamp = parsedConcurrencyStamp;
        return true;
    }

    private ObjectResult ProblemResponse(int status, string title) => Problem(
        statusCode: status,
        title: title,
        detail: "The server-private ATProto session bridge could not complete the request.");
}

public sealed record AtprotoCurrentSessionBridgeResponse(
    string Did,
    string ExpectedPdsUri,
    string OAuthClientKeyId,
    JsonElement OAuthSession);
