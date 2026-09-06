// ABOUTME: Exercises platform identity resolution with hostile and purpose-bound principals.
// ABOUTME: Pins the canonical fallback order and exposes the remaining duplicated caller divergence.

using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Tests.Identity;

public sealed class PlatformIdentityPrincipalExtensionsTests
{
    private const string SubUserId = "11111111-1111-4111-8111-111111111111";
    private const string NameIdentifierUserId = "22222222-2222-4222-8222-222222222222";
    private const string SidUserId = "33333333-3333-4333-8333-333333333333";
    private const string InternalUserId = "44444444-4444-4444-8444-444444444444";

    [Test]
    [Arguments("local")]
    [Arguments("keycloak")]
    [Arguments("atproto")]
    [Arguments("google")]
    public async Task ExplicitAuthenticationAuthorityClaimWinsProviderClassification(
        string provider)
    {
        ClaimsPrincipal principal = Principal(
            "Bearer",
            new Claim("sub", SubUserId),
            new Claim("auth_provider", provider));

        await Assert.That(principal.GetAuthProvider()).IsEqualTo(provider);
    }

    [Test]
    [Arguments(0, SubUserId)]
    [Arguments(1, NameIdentifierUserId)]
    [Arguments(2, SidUserId)]
    [Arguments(3, InternalUserId)]
    public async Task CanonicalResolverUsesEveryDocumentedFallbackPosition(
        int selectedPosition,
        string expectedUserId)
    {
        string[] claimTypes = ["sub", ClaimTypes.NameIdentifier, "sid", "internal_user_id"];
        Claim[] claims = claimTypes
            .Select((claimType, position) => new Claim(
                claimType,
                position == selectedPosition ? expectedUserId : $"malformed-{position}"))
            .ToArray();

        Guid? actual = Principal("Bearer", claims).GetPlatformUserId();

        await Assert.That(actual).IsEqualTo(Guid.Parse(expectedUserId));
    }

    [Test]
    public async Task CanonicalResolverSelectsSubWhenGuidClaimsConflict()
    {
        ClaimsPrincipal principal = Principal(
            "Bearer",
            new Claim("sub", SubUserId),
            new Claim(ClaimTypes.NameIdentifier, NameIdentifierUserId),
            new Claim("sid", SidUserId),
            new Claim("internal_user_id", InternalUserId));

        await Assert.That(principal.GetPlatformUserId()).IsEqualTo(Guid.Parse(SubUserId));
    }

    [Test]
    public async Task CanonicalResolverRejectsUnauthenticatedPrincipalEvenWithGuidClaim()
    {
        ClaimsPrincipal principal = Principal(null, new Claim("sub", SubUserId));

        await Assert.That(principal.GetPlatformUserId()).IsNull();
    }

    [Test]
    public async Task ProviderReadersIgnoreUnauthenticatedIdentityClaims()
    {
        var principal = new ClaimsPrincipal([
            new ClaimsIdentity(authenticationType: "provider"),
            new ClaimsIdentity([
                new Claim("sub", "smuggled-provider-subject"),
                new Claim("email", "smuggled@example.test")
            ])
        ]);

        await Assert.That(principal.GetProviderSubject()).IsNull();
        await Assert.That(principal.GetProviderIdentity()).IsNull();
    }

    [Test]
    public async Task ProviderReadersFailClosedForMultipleAuthenticatedIdentities()
    {
        var principal = new ClaimsPrincipal([
            new ClaimsIdentity([new Claim("sub", "first-provider-subject")], "first"),
            new ClaimsIdentity([new Claim("sub", "second-provider-subject")], "second")
        ]);

        await Assert.That(principal.GetProviderSubject()).IsNull();
        await Assert.That(principal.GetProviderIdentity()).IsNull();
    }

    [Test]
    public async Task ProviderReadersPreserveValidNonGuidProviderIdentity()
    {
        const string subject = "valid-non-guid-provider-subject";
        ClaimsPrincipal principal = Principal(
            "provider",
            new Claim("sub", subject),
            new Claim("iss", "https://accounts.google.com"),
            new Claim("idp", "google"),
            new Claim("email", "provider@example.test"),
            new Claim("email_verified", bool.TrueString));

        await Assert.That(principal.GetProviderSubject()).IsEqualTo(subject);
        await Assert.That(principal.GetProviderIdentity()?.ProviderId).IsEqualTo(
            PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                "https://accounts.google.com",
                subject).Value);
    }

    [Test]
    [Arguments("https://accounts.google.com", AuthenticationProviderKind.Google, "google")]
    [Arguments("https://ACCOUNTS.GOOGLE.COM:443/", AuthenticationProviderKind.Google, "google")]
    [Arguments("https://auth.example.test/realms/events", AuthenticationProviderKind.Keycloak, "keycloak")]
    [Arguments("https://accounts.google.com.example.test", AuthenticationProviderKind.Keycloak, "keycloak")]
    public async Task OidcAccountKeyUsesIssuerAuthorityRatherThanBrokeredProviderHint(
        string issuer,
        AuthenticationProviderKind expectedProviderKind,
        string expectedProvider)
    {
        ClaimsPrincipal principal = Principal(
            "provider",
            new Claim("sub", "exact-subject"),
            new Claim("iss", issuer),
            new Claim("idp", "google"));

        ProviderIdentity? identity = principal.GetProviderIdentity();
        await Assert.That(identity?.AccountKey.ProviderKind)
            .IsEqualTo(expectedProviderKind);
        await Assert.That(identity?.Provider).IsEqualTo(expectedProvider);
    }

    [Test]
    [Arguments("sub", "not-a-guid")]
    [Arguments(ClaimTypes.NameIdentifier, "{not-a-guid}")]
    [Arguments("sid", " ")]
    [Arguments("internal_user_id", "00000000-0000-0000-0000-00000000000z")]
    public async Task CanonicalResolverRejectsMalformedGuidClaim(string claimType, string claimValue)
    {
        ClaimsPrincipal principal = Principal("Bearer", new Claim(claimType, claimValue));

        await Assert.That(principal.GetPlatformUserId()).IsNull();
    }

    [Test]
    public async Task CanonicalResolverFallsThroughNonGuidProviderSubjectToInternalUserId()
    {
        ClaimsPrincipal principal = Principal(
            "Google",
            new Claim("sub", "google-provider-subject-123"),
            new Claim("internal_user_id", InternalUserId));

        await Assert.That(principal.GetPlatformUserId()).IsEqualTo(Guid.Parse(InternalUserId));
    }

    [Test]
    public async Task CanonicalResolverDoesNotReinterpretNonGuidProviderSubjectAsPlatformIdentity()
    {
        ClaimsPrincipal principal = Principal(
            "Atproto",
            new Claim("sub", "did:plc:provider-subject"));

        await Assert.That(principal.GetPlatformUserId()).IsNull();
    }

    [Test]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey, "explore:api-key:owner:id", SubUserId)]
    [Arguments(ApiAuthenticationSchemeNames.SetupSecret, "setup_authority", "active")]
    [Arguments(ApiAuthenticationSchemeNames.AdmissionScanner, "admission_scanner_capability_id", NameIdentifierUserId)]
    [Arguments(ApiAuthenticationSchemeNames.ManagedControlPlane, "managed_instance_id", SidUserId)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoBootstrap, "canonical_actor_id", InternalUserId)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoSession, "did", "did:web:session.example.test")]
    [Arguments("Atproto", "sub", "did:plc:provider-subject")]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt, "privacy_erasure_intent_id", SubUserId)]
    public async Task CanonicalResolverDoesNotReinterpretPurposeBoundSchemeClaims(
        string authenticationScheme,
        string claimType,
        string claimValue)
    {
        ClaimsPrincipal principal = Principal(
            authenticationScheme,
            new Claim(claimType, claimValue));

        await Assert.That(principal.GetPlatformUserId()).IsNull();
    }

    [Test]
    [Category("MigrationAnchor")]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey, "sub", SubUserId)]
    [Arguments(ApiAuthenticationSchemeNames.SetupSecret, ClaimTypes.NameIdentifier, NameIdentifierUserId)]
    [Arguments(ApiAuthenticationSchemeNames.AdmissionScanner, "sid", SidUserId)]
    [Arguments(ApiAuthenticationSchemeNames.ManagedControlPlane, "internal_user_id", InternalUserId)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoBootstrap, "sub", SubUserId)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoSession, ClaimTypes.NameIdentifier, NameIdentifierUserId)]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt, "sid", SidUserId)]
    public async Task MigrationAnchorPurposeBoundSchemeRejectsRecognizedPlatformGuidClaim(
        string purposeBoundScheme,
        string platformClaimType,
        string platformClaimValue)
    {
        var platformClaim = new Claim(platformClaimType, platformClaimValue);
        ClaimsPrincipal bearerPrincipal = Principal("Bearer", platformClaim);
        ClaimsPrincipal purposeBoundPrincipal = Principal(
            purposeBoundScheme,
            new Claim(platformClaimType, platformClaimValue));
        Guid expectedUserId = Guid.Parse(platformClaimValue);

        await Assert.That(bearerPrincipal.GetPlatformUserId())
            .IsEqualTo(expectedUserId)
            .Because("the recognized GUID claim must establish a scheme-sensitive control case.");
        await Assert.That(purposeBoundPrincipal.GetPlatformUserId())
            .IsNull()
            .Because($"{purposeBoundScheme} authority must not become ambient platform-user identity.");
    }

    [Test]
    [Category("MigrationAnchor")]
    public async Task MigrationAnchorMixedBearerAndPurposeBoundIdentityCannotSmugglePlatformGuid()
    {
        var bearerIdentity = new ClaimsIdentity(authenticationType: "Bearer");
        var purposeBoundIdentity = new ClaimsIdentity(
            [new Claim(PlatformIdentityClaimTypes.InternalUserId, InternalUserId)],
            ApiAuthenticationSchemeNames.ApiKey);
        var principal = new ClaimsPrincipal([bearerIdentity, purposeBoundIdentity]);

        await Assert.That(principal.GetPlatformUserId())
            .IsNull()
            .Because("ambient identity must not read recognized claims from an excluded identity.");
    }

    [Test]
    [Category("MigrationAnchor")]
    public async Task MigrationAnchorCurrentUserServiceMatchesCanonicalConflictingClaimPriority()
    {
        ClaimsPrincipal principal = Principal(
            "Bearer",
            new Claim("sub", SubUserId),
            new Claim(ClaimTypes.NameIdentifier, NameIdentifierUserId),
            new Claim("sid", SidUserId),
            new Claim("internal_user_id", InternalUserId));
        Guid? canonicalUserId = principal.GetPlatformUserId();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        var duplicatedCaller = new CurrentUserService(accessor);

        await Assert.That(canonicalUserId).IsEqualTo(Guid.Parse(SubUserId));
        await Assert.That(duplicatedCaller.UserId)
            .IsEqualTo(canonicalUserId)
            .Because("CurrentUserService must preserve sub -> nameidentifier -> sid -> internal_user_id priority.");
    }

    private static ClaimsPrincipal Principal(string? authenticationType, params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType));
}
