using Explore.Domain.Enums;
using Explore.Domain.Services.Identity;

namespace Event.Domain.UnitTests;

public sealed class IdentityCorrelationPolicyTests
{
    private const string TrustedIssuer = "https://identity.example.test/realms/trusted";
    private static readonly HashSet<string> Trusted = new(StringComparer.Ordinal) { TrustedIssuer };

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ExactBindingWinsOverEmailAndTrustChanges(bool trusted)
    {
        IdentityCorrelationDecision result = IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: true, bindingConflict: false,
            provider: AuthenticationProviderKind.Keycloak, normalizedIssuer: TrustedIssuer,
            hasVerifiedEmail: true, trustedIssuers: trusted ? Trusted : new HashSet<string>(),
            emailMatchCount: 2, candidateLocalOwned: true);

        await Assert.That(result).IsEqualTo(IdentityCorrelationDecision.ExactBinding);
    }

    [Test]
    [Arguments(TrustedIssuer, IdentityCorrelationDecision.Correlate)]
    [Arguments("https://identity.example.test/realms/other", IdentityCorrelationDecision.Enroll)]
    [Arguments("https://identity.example.test/realms/trusted/child", IdentityCorrelationDecision.Enroll)]
    [Arguments("https://identity.example.test/realms/Trusted", IdentityCorrelationDecision.Enroll)]
    public async Task SameProviderKindDoesNotConferAnotherIssuersTrust(
        string issuer, IdentityCorrelationDecision expected)
    {
        await Assert.That(IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: false, bindingConflict: false,
            provider: AuthenticationProviderKind.Keycloak, normalizedIssuer: issuer,
            hasVerifiedEmail: true, trustedIssuers: Trusted,
            emailMatchCount: 1, candidateLocalOwned: false)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null, true)]
    [Arguments(TrustedIssuer, false)]
    public async Task MissingAuthorityOrVerificationCannotAdoptButAllowsSafeSignup(
        string? issuer, bool verified)
    {
        await Assert.That(IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: false, bindingConflict: false,
            provider: AuthenticationProviderKind.Keycloak, normalizedIssuer: issuer,
            hasVerifiedEmail: verified, trustedIssuers: Trusted,
            emailMatchCount: 1, candidateLocalOwned: false))
            .IsEqualTo(IdentityCorrelationDecision.Enroll);
    }

    [Test]
    public async Task TrustedVerifiedUnmatchedIdentityMayEnroll()
    {
        await Assert.That(IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: false, bindingConflict: false,
            provider: AuthenticationProviderKind.Keycloak, normalizedIssuer: TrustedIssuer,
            hasVerifiedEmail: true, trustedIssuers: Trusted,
            emailMatchCount: 0, candidateLocalOwned: false))
            .IsEqualTo(IdentityCorrelationDecision.Enroll);
    }

    [Test]
    public async Task LocalOwnedCandidateCannotBeAutomaticallyAdopted()
    {
        await Assert.That(IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: false, bindingConflict: false,
            provider: AuthenticationProviderKind.Keycloak, normalizedIssuer: TrustedIssuer,
            hasVerifiedEmail: true, trustedIssuers: Trusted,
            emailMatchCount: 1, candidateLocalOwned: true))
            .IsEqualTo(IdentityCorrelationDecision.Enroll);
    }

    [Test]
    [Arguments(true, 0)]
    [Arguments(false, 2)]
    public async Task ConflictingBindingOrAmbiguousAdoptionCannotBecomeSignup(bool conflict, int matches)
    {
        await Assert.That(IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: false, bindingConflict: conflict,
            provider: AuthenticationProviderKind.Keycloak, normalizedIssuer: TrustedIssuer,
            hasVerifiedEmail: true, trustedIssuers: Trusted,
            emailMatchCount: matches, candidateLocalOwned: false))
            .IsEqualTo(IdentityCorrelationDecision.RecoveryRequired);
    }

    [Test]
    public async Task LocalEnrollmentRequiresItsOwnLifecycleAuthority()
    {
        await Assert.That(IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: false, bindingConflict: false,
            provider: AuthenticationProviderKind.Local, normalizedIssuer: TrustedIssuer,
            hasVerifiedEmail: true, trustedIssuers: Trusted,
            emailMatchCount: 1, candidateLocalOwned: false))
            .IsEqualTo(IdentityCorrelationDecision.RecoveryRequired);
    }

    [Test]
    public async Task AtprotoCannotGainEmailAuthorityButKeepsEmailFreeEnrollment()
    {
        await Assert.That(IdentityCorrelationPolicy.Evaluate(
            hasExactBinding: false, bindingConflict: false,
            provider: AuthenticationProviderKind.Atproto, normalizedIssuer: TrustedIssuer,
            hasVerifiedEmail: true, trustedIssuers: Trusted,
            emailMatchCount: 1, candidateLocalOwned: false))
            .IsEqualTo(IdentityCorrelationDecision.Enroll);
    }
}
