using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Authentication.Atproto.Models;

public sealed record AtprotoOAuthVerificationInput
{
    public AtprotoOAuthVerificationInput(
        AtprotoDid ExpectedDid,
        Uri ExpectedPdsUri,
        string OAuthClientKeyId,
        ReadOnlyMemory<byte> OAuthSessionPayload)
    {
        this.ExpectedDid = ExpectedDid;
        this.ExpectedPdsUri = ExpectedPdsUri;
        this.OAuthClientKeyId = OAuthClientKeyId;
        this.OAuthSessionPayload = OAuthSessionPayload.ToArray();
    }

    public AtprotoDid ExpectedDid { get; }
    public Uri ExpectedPdsUri { get; }
    public string OAuthClientKeyId { get; }
    public ReadOnlyMemory<byte> OAuthSessionPayload { get; }
}

public sealed record AtprotoVerifiedOAuthSession
{
    public AtprotoVerifiedOAuthSession(
        AtprotoDid Did,
        string Handle,
        Uri PdsUri,
        string OAuthClientKeyId,
        ReadOnlyMemory<byte> OAuthSessionPayload)
    {
        this.Did = Did;
        this.Handle = Handle;
        this.PdsUri = PdsUri;
        this.OAuthClientKeyId = OAuthClientKeyId;
        this.OAuthSessionPayload = OAuthSessionPayload.ToArray();
    }

    public AtprotoDid Did { get; }
    public string Handle { get; }
    public Uri PdsUri { get; }
    public string OAuthClientKeyId { get; }
    public ReadOnlyMemory<byte> OAuthSessionPayload { get; }
}

public sealed record AtprotoPreparedOAuthSession
{
    public AtprotoPreparedOAuthSession(
        ReadOnlyMemory<byte> SessionCiphertext,
        string EncryptionKeyId,
        int EnvelopeVersion,
        Guid TenantId,
        Guid UserId,
        AtprotoDid SubjectDid,
        string PdsHost,
        string OAuthClientKeyId,
        DateTime? ExpiresAt)
    {
        this.SessionCiphertext = SessionCiphertext.ToArray();
        this.EncryptionKeyId = EncryptionKeyId;
        this.EnvelopeVersion = EnvelopeVersion;
        this.TenantId = TenantId;
        this.UserId = UserId;
        this.SubjectDid = SubjectDid;
        this.PdsHost = PdsHost;
        this.OAuthClientKeyId = OAuthClientKeyId;
        this.ExpiresAt = ExpiresAt;
    }

    public ReadOnlyMemory<byte> SessionCiphertext { get; }
    public string EncryptionKeyId { get; }
    public int EnvelopeVersion { get; }
    public Guid TenantId { get; }
    public Guid UserId { get; }
    public AtprotoDid SubjectDid { get; }
    public string PdsHost { get; }
    public string OAuthClientKeyId { get; }
    public DateTime? ExpiresAt { get; }
}

public sealed record AtprotoOAuthVerificationResult(
    AtprotoVerifiedOAuthSession? Session,
    string? FailureCode)
{
    public static AtprotoOAuthVerificationResult Verified(AtprotoVerifiedOAuthSession session) => new(session, null);
    public static AtprotoOAuthVerificationResult Failed(string code) => new(null, code);
}

public sealed record AtprotoIssuedSessionToken(string Token, DateTimeOffset ExpiresAt);

public sealed record AtprotoOAuthRefreshResult(bool Success, string FailureCode)
{
    public static AtprotoOAuthRefreshResult Refreshed() => new(true, string.Empty);

    public static AtprotoOAuthRefreshResult ReauthenticationRequired() =>
        new(false, "reauthentication_required");
}

public sealed record AtprotoSessionRefreshResult(
    bool Success,
    string FailureCode,
    string? Token = null,
    DateTimeOffset? ExpiresAt = null)
{
    public static AtprotoSessionRefreshResult Failed(string code) => new(false, code);

    public static AtprotoSessionRefreshResult Succeeded(AtprotoIssuedSessionToken token) =>
        new(true, string.Empty, token.Token, token.ExpiresAt);
}

public sealed record AtprotoSessionBootstrapResult(
    bool Success,
    string FailureCode,
    Guid? UserId = null,
    Guid? ActorId = null,
    Guid? ParticipationId = null,
    AtprotoSubjectClassification? Classification = null,
    string? Token = null,
    DateTimeOffset? ExpiresAt = null,
    Guid? CanonicalActorId = null,
    Guid? ExpectedCanonicalActorConcurrencyStamp = null)
{
    public static AtprotoSessionBootstrapResult Failed(string code) => new(false, code);

    public static AtprotoSessionBootstrapResult Succeeded(
        Guid userId,
        Guid actorId,
        Guid? participationId,
        AtprotoSubjectClassification classification,
        AtprotoIssuedSessionToken token,
        Guid? canonicalActorId = null,
        Guid? expectedCanonicalActorConcurrencyStamp = null) =>
        new(
            true,
            string.Empty,
            userId,
            actorId,
            participationId,
            classification,
            token.Token,
            token.ExpiresAt,
            canonicalActorId,
            expectedCanonicalActorConcurrencyStamp);
}
