// ABOUTME: Exercises platform identity resolution with hostile and purpose-bound principals.
// ABOUTME: Pins the canonical fallback order and exposes the remaining duplicated caller divergence.

using System.Security.Claims;
using System.Globalization;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Tests.Identity;

public sealed class PlatformIdentityPrincipalExtensionsTests
{
    public enum InvalidLocalSessionPrincipal
    {
        OtherAuthenticationScheme,
        Unauthenticated,
        MultipleAuthenticatedIdentities,
        NoncanonicalSubject,
        UppercaseSubject,
        MalformedSubject,
        EmptySubject,
        EmptyGuidSubject,
        OtherIssuer,
        OtherAudience,
        OtherProvider,
        BlankStamp,
        OversizedStamp,
        ReplacementPurpose
    }

    public enum InvalidLocalProviderProjection
    {
        MissingIssuer,
        OtherIssuer,
        NoncanonicalSubject,
        UppercaseSubject,
        PaddedSubject,
        MalformedSubject,
        EmptySubject,
        EmptyGuidSubject
    }

    public enum InvalidReplacementPrincipal
    {
        OrdinaryScheme,
        UnauthenticatedOnly,
        MixedAuthenticatedIdentities,
        DuplicateRequiredClaim,
        UnauthenticatedClaimDonation
    }

    private const string SubUserId = "11111111-1111-4111-8111-111111111111";
    private const string NameIdentifierUserId = "22222222-2222-4222-8222-222222222222";
    private const string SidUserId = "33333333-3333-4333-8333-333333333333";
    private const string InternalUserId = "44444444-4444-4444-8444-444444444444";

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LocalSessionAuthorityComesOnlyFromTheSelectedAuthenticatedLocalIdentity(bool emailVerified)
    {
        Guid subjectId = Guid.CreateVersion7();
        string stamp = Guid.CreateVersion7().ToString("N");
        var principal = new ClaimsPrincipal([
            new ClaimsIdentity(LocalSessionClaims(subjectId: subjectId, securityStamp: stamp, emailVerified: emailVerified), ApiAuthenticationSchemeNames.LocalIdentity),
            new ClaimsIdentity(LocalSessionClaims(
                subjectId: Guid.CreateVersion7(), securityStamp: Guid.CreateVersion7().ToString("N"), emailVerified: !emailVerified))
        ]);

        LocalSessionAuthority? authority = principal.TryGetLocalSessionAuthority();

        await Assert.That(authority).IsNotNull();
        await Assert.That(authority!.LocalSubjectId).IsEqualTo(subjectId);
        await Assert.That(authority.EmailVerified).IsEqualTo(emailVerified);
        await Assert.That(string.Equals(authority.SecurityStamp, stamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments("sub")]
    [Arguments(LocalSessionToken.SecurityStampClaim)]
    [Arguments("iss")]
    [Arguments("aud")]
    [Arguments("auth_provider")]
    [Arguments("email_verified")]
    public async Task LocalSessionRequiredClaimsCannotBeMissingDuplicatedOrDonated(string claimType)
    {
        Claim[] claims = LocalSessionClaims(
            subjectId: Guid.CreateVersion7(), securityStamp: Guid.CreateVersion7().ToString("N"), emailVerified: true);
        Claim requiredClaim = claims.Single(claim => claim.Type == claimType);
        Claim[] incomplete = claims.Where(claim => claim.Type != claimType).ToArray();
        var missing = Principal(ApiAuthenticationSchemeNames.LocalIdentity, incomplete);
        var duplicated = Principal(ApiAuthenticationSchemeNames.LocalIdentity, [.. claims, requiredClaim]);
        var donated = new ClaimsPrincipal([
            new ClaimsIdentity(incomplete, ApiAuthenticationSchemeNames.LocalIdentity),
            new ClaimsIdentity([requiredClaim])
        ]);

        await Assert.That(missing.TryGetLocalSessionAuthority()).IsNull();
        await Assert.That(duplicated.TryGetLocalSessionAuthority()).IsNull();
        await Assert.That(donated.TryGetLocalSessionAuthority()).IsNull();
    }

    [Test]
    [Arguments(InvalidLocalSessionPrincipal.OtherAuthenticationScheme)]
    [Arguments(InvalidLocalSessionPrincipal.Unauthenticated)]
    [Arguments(InvalidLocalSessionPrincipal.MultipleAuthenticatedIdentities)]
    [Arguments(InvalidLocalSessionPrincipal.NoncanonicalSubject)]
    [Arguments(InvalidLocalSessionPrincipal.UppercaseSubject)]
    [Arguments(InvalidLocalSessionPrincipal.MalformedSubject)]
    [Arguments(InvalidLocalSessionPrincipal.EmptySubject)]
    [Arguments(InvalidLocalSessionPrincipal.EmptyGuidSubject)]
    [Arguments(InvalidLocalSessionPrincipal.OtherIssuer)]
    [Arguments(InvalidLocalSessionPrincipal.OtherAudience)]
    [Arguments(InvalidLocalSessionPrincipal.OtherProvider)]
    [Arguments(InvalidLocalSessionPrincipal.BlankStamp)]
    [Arguments(InvalidLocalSessionPrincipal.OversizedStamp)]
    [Arguments(InvalidLocalSessionPrincipal.ReplacementPurpose)]
    public async Task LocalSessionAuthorityRejectsMalformedOrMixedAuthority(InvalidLocalSessionPrincipal defect)
    {
        Guid subjectId = Guid.Parse("abcdefab-cdef-4abc-8def-abcdefabcdef");
        List<Claim> claims = LocalSessionClaims(
            subjectId: subjectId, securityStamp: Guid.CreateVersion7().ToString("N"), emailVerified: true).ToList();
        (string? ClaimType, string? Value) replacement = defect switch
        {
            InvalidLocalSessionPrincipal.NoncanonicalSubject => ("sub", subjectId.ToString("N")),
            InvalidLocalSessionPrincipal.UppercaseSubject => ("sub", subjectId.ToString("D").ToUpperInvariant()),
            InvalidLocalSessionPrincipal.MalformedSubject => ("sub", "invalid-subject"),
            InvalidLocalSessionPrincipal.EmptySubject => ("sub", string.Empty),
            InvalidLocalSessionPrincipal.EmptyGuidSubject => ("sub", Guid.Empty.ToString("D")),
            InvalidLocalSessionPrincipal.OtherIssuer => ("iss", "https://untrusted.example.test"),
            InvalidLocalSessionPrincipal.OtherAudience => ("aud", LocalCredentialChallengeToken.Audience),
            InvalidLocalSessionPrincipal.OtherProvider => ("auth_provider", "Local"),
            InvalidLocalSessionPrincipal.BlankStamp => (LocalSessionToken.SecurityStampClaim, " "),
            InvalidLocalSessionPrincipal.OversizedStamp => (LocalSessionToken.SecurityStampClaim,
                Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(129))),
            _ => (null, null)
        };
        if (replacement.ClaimType is not null)
        {
            claims.RemoveAll(claim => claim.Type == replacement.ClaimType);
            claims.Add(new Claim(replacement.ClaimType, replacement.Value!));
        }
        if (defect == InvalidLocalSessionPrincipal.ReplacementPurpose)
        {
            claims.Add(new Claim(LocalCredentialChallengeToken.PurposeClaim, LocalCredentialChallengeToken.Purpose));
        }
        ClaimsPrincipal principal = defect switch
        {
            InvalidLocalSessionPrincipal.OtherAuthenticationScheme => Principal("Bearer", claims.ToArray()),
            InvalidLocalSessionPrincipal.Unauthenticated => Principal(null, claims.ToArray()),
            InvalidLocalSessionPrincipal.MultipleAuthenticatedIdentities => new ClaimsPrincipal([
                new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.LocalIdentity),
                new ClaimsIdentity(authenticationType: "Bearer")
            ]),
            _ => Principal(ApiAuthenticationSchemeNames.LocalIdentity, claims.ToArray())
        };

        await Assert.That(principal.TryGetLocalSessionAuthority()).IsNull();
    }

    [Test]
    [Arguments("True")]
    [Arguments("False")]
    [Arguments(" true ")]
    [Arguments(" false ")]
    [Arguments("1")]
    [Arguments("")]
    public async Task LocalSessionAuthorityRejectsNoncanonicalVerificationBoolean(string verificationValue)
    {
        List<Claim> claims = LocalSessionClaims(
            subjectId: Guid.CreateVersion7(), securityStamp: Guid.CreateVersion7().ToString("N"), emailVerified: true).ToList();
        claims.RemoveAll(claim => claim.Type == "email_verified");
        claims.Add(new Claim("email_verified", verificationValue));
        ClaimsPrincipal principal = Principal(ApiAuthenticationSchemeNames.LocalIdentity, claims.ToArray());

        await Assert.That(principal.TryGetLocalSessionAuthority()).IsNull();
    }

    private static Claim[] LocalSessionClaims(Guid subjectId, string securityStamp, bool emailVerified) =>
    [
        new("sub", subjectId.ToString("D")),
        new(LocalSessionToken.SecurityStampClaim, securityStamp),
        new("iss", LocalIdentityOptions.Issuer),
        new("aud", LocalIdentityOptions.Audience),
        new("auth_provider", "local"),
        new("email_verified", emailVerified ? "true" : "false")
    ];

    [Test]
    public async Task NativeLocalIssuerProjectsCanonicalSubjectAsExactLocalAccountKey()
    {
        string subject = Guid.CreateVersion7().ToString("D");
        ClaimsPrincipal principal = Principal(ApiAuthenticationSchemeNames.LocalIdentity,
            new Claim("iss", LocalIdentityOptions.Issuer), new Claim("sub", subject),
            new Claim("auth_provider", "local"), new Claim("email_verified", bool.TrueString),
            new Claim(ClaimTypes.NameIdentifier, NameIdentifierUserId), new Claim("sid", SidUserId),
            new Claim(PlatformIdentityClaimTypes.InternalUserId, InternalUserId));

        ProviderIdentity? identity = principal.GetProviderIdentity();

        await Assert.That(identity).IsNotNull();
        await Assert.That(identity!.AccountKey.ProviderKind).IsEqualTo(AuthenticationProviderKind.Local);
        await Assert.That(identity.AccountKey.Value).IsEqualTo(subject);
        await Assert.That(identity.Subject).IsEqualTo(subject);
        await Assert.That(principal.GetProviderId(providerSubject: subject, provider: "local")).IsEqualTo(subject);
        await Assert.That(principal.GetPlatformUserId()).IsEqualTo(Guid.Parse(subject));
    }

    [Test]
    [Arguments(InvalidLocalProviderProjection.MissingIssuer)]
    [Arguments(InvalidLocalProviderProjection.OtherIssuer)]
    [Arguments(InvalidLocalProviderProjection.NoncanonicalSubject)]
    [Arguments(InvalidLocalProviderProjection.UppercaseSubject)]
    [Arguments(InvalidLocalProviderProjection.PaddedSubject)]
    [Arguments(InvalidLocalProviderProjection.MalformedSubject)]
    [Arguments(InvalidLocalProviderProjection.EmptySubject)]
    [Arguments(InvalidLocalProviderProjection.EmptyGuidSubject)]
    public async Task LocalProviderProjectionRejectsUntrustedIssuerOrNoncanonicalSubject(InvalidLocalProviderProjection defect)
    {
        Guid subjectId = Guid.Parse("abcdefab-cdef-4abc-8def-abcdefabcdef");
        string subject = defect switch
        {
            InvalidLocalProviderProjection.NoncanonicalSubject => subjectId.ToString("N"),
            InvalidLocalProviderProjection.UppercaseSubject => subjectId.ToString("D").ToUpperInvariant(),
            InvalidLocalProviderProjection.PaddedSubject => $" {subjectId:D} ",
            InvalidLocalProviderProjection.MalformedSubject => "not-a-local-subject",
            InvalidLocalProviderProjection.EmptySubject => string.Empty,
            InvalidLocalProviderProjection.EmptyGuidSubject => Guid.Empty.ToString("D"),
            _ => subjectId.ToString("D")
        };
        List<Claim> claims =
        [
            new("sub", subject), new("auth_provider", "local"),
            new(ClaimTypes.NameIdentifier, NameIdentifierUserId), new("sid", SidUserId),
            new(PlatformIdentityClaimTypes.InternalUserId, InternalUserId)
        ];
        if (defect != InvalidLocalProviderProjection.MissingIssuer)
            claims.Add(new Claim("iss", defect == InvalidLocalProviderProjection.OtherIssuer
                ? "https://untrusted.example.test/realms/other" : LocalIdentityOptions.Issuer));
        ClaimsPrincipal principal = Principal(ApiAuthenticationSchemeNames.LocalIdentity, claims.ToArray());

        await Assert.That(principal.GetProviderIdentity()).IsNull();
        await Assert.That(principal.GetPlatformUserId()).IsNull();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            _ = principal.GetRequiredPlatformUserId();
            return Task.CompletedTask;
        });
    }

    [Test]
    [Arguments(InvalidLocalProviderProjection.MissingIssuer)]
    [Arguments(InvalidLocalProviderProjection.OtherIssuer)]
    public async Task InvalidLocalIssuerCannotRegainPlatformIdentityThroughGuidFallback(InvalidLocalProviderProjection defect)
    {
        List<Claim> claims =
        [
            new("sub", SubUserId),
            new("auth_provider", "local"),
            new(PlatformIdentityClaimTypes.InternalUserId, InternalUserId)
        ];
        if (defect == InvalidLocalProviderProjection.OtherIssuer)
            claims.Add(new Claim("iss", "https://trusted.example.test/realms/external"));
        ClaimsPrincipal principal = Principal("Bearer", claims.ToArray());

        await Assert.That(principal.GetProviderIdentity()).IsNull();
        await Assert.That(principal.GetPlatformUserId()).IsNull();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            _ = principal.GetRequiredPlatformUserId();
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task DedicatedReplacementIdentityProjectsOnlyItsOwnNominalAuthority()
    {
        Guid subjectId = Guid.CreateVersion7();
        Guid operationId = Guid.CreateVersion7();
        string stamp = Guid.CreateVersion7().ToString("N");
        DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        ClaimsPrincipal principal = Principal(ApiAuthenticationSchemeNames.LocalCredentialReplacement,
            ReplacementClaims(subjectId: subjectId, operationId: operationId, securityStamp: stamp, issuedAt: issuedAt));

        LocalCredentialReplacementAuthority? authority = principal.TryGetLocalCredentialReplacementAuthority();

        await Assert.That(authority).IsNotNull();
        await Assert.That(authority!.Subject.LocalSubjectId).IsEqualTo(subjectId);
        await Assert.That(authority.Subject.OperationId).IsEqualTo(operationId);
        await Assert.That(string.Equals(authority.Subject.SecurityStamp, stamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(authority.IssuedAtUtc).IsEqualTo(issuedAt);
        await Assert.That(authority.ExpiresAtUtc).IsEqualTo(issuedAt.AddMinutes(5));
        await Assert.That(principal.GetPlatformUserId()).IsNull();
        await Assert.That(principal.GetProviderIdentity()).IsNull();
    }

    [Test]
    [Arguments(InvalidReplacementPrincipal.OrdinaryScheme)]
    [Arguments(InvalidReplacementPrincipal.UnauthenticatedOnly)]
    [Arguments(InvalidReplacementPrincipal.MixedAuthenticatedIdentities)]
    [Arguments(InvalidReplacementPrincipal.DuplicateRequiredClaim)]
    [Arguments(InvalidReplacementPrincipal.UnauthenticatedClaimDonation)]
    public async Task ReplacementAuthorityCannotBeAssembledFromUntrustedOrMixedIdentities(InvalidReplacementPrincipal defect)
    {
        string stamp = Guid.CreateVersion7().ToString("N");
        List<Claim> claims = ReplacementClaims(subjectId: Guid.CreateVersion7(), operationId: Guid.CreateVersion7(),
            securityStamp: stamp, issuedAt: DateTimeOffset.UtcNow).ToList();
        if (defect == InvalidReplacementPrincipal.DuplicateRequiredClaim)
            claims.Add(new Claim(LocalCredentialChallengeToken.SecurityStampClaim, stamp));
        if (defect == InvalidReplacementPrincipal.UnauthenticatedClaimDonation)
            claims.RemoveAll(claim => claim.Type == LocalCredentialChallengeToken.SecurityStampClaim);
        ClaimsPrincipal principal = defect switch
        {
            InvalidReplacementPrincipal.OrdinaryScheme => Principal(ApiAuthenticationSchemeNames.LocalIdentity, claims.ToArray()),
            InvalidReplacementPrincipal.UnauthenticatedOnly => Principal(null, claims.ToArray()),
            InvalidReplacementPrincipal.MixedAuthenticatedIdentities => new ClaimsPrincipal([
                new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.LocalCredentialReplacement),
                new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString("D"))], ApiAuthenticationSchemeNames.LocalIdentity)
            ]),
            InvalidReplacementPrincipal.UnauthenticatedClaimDonation => new ClaimsPrincipal([
                new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.LocalCredentialReplacement),
                new ClaimsIdentity([new Claim(LocalCredentialChallengeToken.SecurityStampClaim, stamp)])
            ]),
            InvalidReplacementPrincipal.DuplicateRequiredClaim => Principal(ApiAuthenticationSchemeNames.LocalCredentialReplacement, claims.ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        await Assert.That(principal.TryGetLocalCredentialReplacementAuthority()).IsNull();
    }

    private static Claim[] ReplacementClaims(Guid subjectId, Guid operationId, string securityStamp, DateTimeOffset issuedAt) =>
    [
        new("sub", subjectId.ToString("D")),
        new(LocalCredentialChallengeToken.OperationIdClaim, operationId.ToString("D")),
        new(LocalCredentialChallengeToken.SecurityStampClaim, securityStamp),
        new("jti", Guid.CreateVersion7().ToString("N")),
        new("iss", LocalIdentityOptions.Issuer),
        new("aud", LocalCredentialChallengeToken.Audience),
        new(LocalCredentialChallengeToken.PurposeClaim, LocalCredentialChallengeToken.Purpose),
        new("iat", issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
        new("nbf", issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
        new("exp", issuedAt.AddMinutes(5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
    ];

    [Test]
    [Arguments("keycloak", null)]
    [Arguments("keycloak", "verified")]
    [Arguments("google", null)]
    [Arguments("google", "verified")]
    [Arguments("local", null)]
    [Arguments("local", "verified")]
    [Arguments("unknown", null)]
    [Arguments("unknown", "verified")]
    public async Task ProviderNameAndEmailCannotReplaceMissingOrMalformedVerificationEvidence(
        string provider,
        string? verificationClaim)
    {
        const string email = "provider@example.test";
        List<Claim> claims =
        [
            new("sub", SubUserId),
            new("auth_provider", provider),
            new("email", email)
        ];
        if (verificationClaim is not null)
        {
            claims.Add(new Claim("email_verified", verificationClaim));
        }

        ClaimsPrincipal principal = Principal("Bearer", claims.ToArray());

        await Assert.That(principal.GetEmailVerified()).IsFalse();
    }

    [Test]
    [Arguments("keycloak", true)]
    [Arguments("keycloak", false)]
    [Arguments("google", true)]
    [Arguments("google", false)]
    [Arguments("local", true)]
    [Arguments("local", false)]
    [Arguments("unknown", true)]
    [Arguments("unknown", false)]
    public async Task ExplicitVerificationEvidenceIsPreservedForAuthenticatedProvider(
        string provider,
        bool verified)
    {
        const string email = "provider@example.test";
        ClaimsPrincipal principal = Principal(
            "Bearer",
            new Claim("sub", SubUserId),
            new Claim("auth_provider", provider),
            new Claim("email", email),
            new Claim("email_verified", verified.ToString()));

        await Assert.That(principal.GetEmailVerified()).IsEqualTo(verified);
    }

    [Test]
    [Arguments("keycloak")]
    [Arguments("google")]
    [Arguments("local")]
    [Arguments("unknown")]
    public async Task MissingAmbientIdentityCannotImplyEmailVerification(string provider)
    {
        ClaimsPrincipal principal = Principal(
            null,
            new Claim("auth_provider", provider),
            new Claim("email", "provider@example.test"),
            new Claim("email_verified", bool.TrueString));

        await Assert.That(principal.GetEmailVerified()).IsFalse();
    }

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
