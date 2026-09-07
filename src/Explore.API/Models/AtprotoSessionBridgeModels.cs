using System.Text.Json;

namespace Explore.API.Models;

public sealed record BffAtprotoSessionBridgeRequest(
    string ExpectedDid,
    string ExpectedPdsUri,
    string OAuthClientKeyId,
    string Classification,
    JsonElement OAuthSession,
    Guid? CanonicalActorId,
    Guid? ExpectedCanonicalActorConcurrencyStamp);

public sealed record BffAtprotoSessionBridgeResponse(
    Guid UserId,
    Guid ActorId,
    Guid ParticipationId,
    string Did,
    string Classification,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid? CanonicalActorId,
    Guid? ExpectedCanonicalActorConcurrencyStamp);

public sealed record BffAtprotoSessionRefreshResponse(
    Guid UserId,
    string Did,
    string AccessToken,
    DateTimeOffset ExpiresAt);
