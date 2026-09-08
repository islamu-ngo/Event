using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Authentication.Atproto.Models;

public sealed record AtprotoCurrentSessionIdentity(
    Guid TenantId,
    Guid UserId,
    AtprotoDid Did);

public sealed record AtprotoCurrentOAuthSession
{
    public AtprotoCurrentOAuthSession(
        AtprotoDid Did,
        Uri ExpectedPdsUri,
        string OAuthClientKeyId,
        ReadOnlyMemory<byte> OAuthSessionPayload)
    {
        this.Did = Did;
        this.ExpectedPdsUri = ExpectedPdsUri;
        this.OAuthClientKeyId = OAuthClientKeyId;
        this.OAuthSessionPayload = OAuthSessionPayload.ToArray();
    }

    public AtprotoDid Did { get; }
    public Uri ExpectedPdsUri { get; }
    public string OAuthClientKeyId { get; }
    public ReadOnlyMemory<byte> OAuthSessionPayload { get; }
}
