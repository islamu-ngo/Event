// ABOUTME: Carries one protected ATProto OAuth flow binding across CarpaNet state consumption and session storage.
// ABOUTME: Prevents issuer, tenant, DID, PDS, origin, return-path, and signing-key substitution during callback.

namespace Explore.Blazor.Services.Auth;

public sealed record AtprotoOAuthFlowSeed(
    string ExpectedDid,
    Uri ExpectedPdsUri,
    Guid TenantId,
    string TenantSlug,
    Uri Origin,
    string ReturnPath,
    string OAuthClientKeyId,
    string Classification,
    Guid? CanonicalActorId = null,
    Guid? ExpectedCanonicalActorConcurrencyStamp = null)
{
    public required BffAtprotoBrowserBinding BrowserBinding { get; init; }
}

public sealed record AtprotoOAuthFlowBinding(
    AtprotoOAuthFlowSeed Seed,
    Uri Issuer);

internal static class AtprotoOAuthFlowValidation
{
    internal static void Validate(AtprotoOAuthFlowSeed seed, AtprotoTenantOriginResolver origins, AtprotoBrowserProof proof)
    {
        if (seed.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(seed.ExpectedDid)
            || seed.ExpectedDid.Length > 2048 || !seed.ExpectedDid.StartsWith("did:", StringComparison.Ordinal)
            || seed.ExpectedDid.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || string.IsNullOrWhiteSpace(seed.TenantSlug) || string.IsNullOrWhiteSpace(seed.OAuthClientKeyId)
            || seed.OAuthClientKeyId.Length > 128
            || seed.CanonicalActorId.HasValue != seed.ExpectedCanonicalActorConcurrencyStamp.HasValue
            || seed.CanonicalActorId == Guid.Empty || seed.ExpectedCanonicalActorConcurrencyStamp == Guid.Empty
            || !IsSafeReturnPath(seed.ReturnPath) || !IsHttpsOrigin(seed.ExpectedPdsUri) || !IsHttpsOrigin(seed.Origin)
            || !proof.IsLive(seed.BrowserBinding)
            || AtprotoSubjectClassifications.Normalize(seed.Classification) != seed.Classification)
            throw new InvalidOperationException("ATProto OAuth flow binding is invalid.");
        var configured = origins.Resolve(seed.Origin);
        if (configured.TenantId != seed.TenantId || configured.TenantSlug != seed.TenantSlug)
            throw new InvalidOperationException("ATProto OAuth tenant binding is invalid.");
    }

    internal static bool IsHttpsOrigin(Uri? uri) => uri is { IsAbsoluteUri: true }
        && uri.Scheme == Uri.UriSchemeHttps && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static bool IsSafeReturnPath(string? value) => value is { Length: > 0 and <= 2048 }
        && value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.StartsWith("/\\", StringComparison.Ordinal) && !value.Any(char.IsControl);
}

public sealed record AtprotoBffSessionResult(
    Guid UserId,
    Guid ActorId,
    Guid ParticipationId,
    string Did,
    string Classification,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid? CanonicalActorId = null,
    Guid? ExpectedCanonicalActorConcurrencyStamp = null);

public static class AtprotoSubjectClassifications
{
    public const string Person = "person";
    public const string Organization = "organization";
    public const string Group = "group";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Person => Person,
        Organization => Organization,
        Group => Group,
        _ => throw new InvalidOperationException("ATProto subject classification is invalid.")
    };
}

public sealed class AtprotoOAuthFlowContext
{
    public AtprotoOAuthFlowBinding? Binding { get; private set; }

    public AtprotoBffSessionResult? SessionResult { get; private set; }

    public void BindConsumedState(AtprotoOAuthFlowBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (Binding is not null)
        {
            throw new InvalidOperationException("ATProto OAuth state was already consumed in this request.");
        }

        Binding = binding;
    }

    public void CaptureSession(AtprotoBffSessionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Binding is null || SessionResult is not null)
        {
            throw new InvalidOperationException("ATProto OAuth session callback ordering is invalid.");
        }

        SessionResult = result;
    }
}
