using Explore.Domain.Enums;

namespace Explore.Domain.Services.Identity;

public enum IdentityCorrelationDecision
{
    ExactBinding,
    Correlate,
    Enroll,
    RecoveryRequired
}

public static class IdentityCorrelationPolicy
{
    public static IdentityCorrelationDecision Evaluate(
        bool hasExactBinding,
        bool bindingConflict,
        AuthenticationProviderKind provider,
        string? normalizedIssuer,
        bool hasVerifiedEmail,
        IReadOnlySet<string> trustedIssuers,
        int emailMatchCount,
        bool candidateLocalOwned)
    {
        if (bindingConflict)
            return IdentityCorrelationDecision.RecoveryRequired;
        if (hasExactBinding)
            return IdentityCorrelationDecision.ExactBinding;
        if (provider == AuthenticationProviderKind.Local)
            return IdentityCorrelationDecision.RecoveryRequired;
        if (!CanCorrelate(provider, normalizedIssuer, hasVerifiedEmail, trustedIssuers))
            return IdentityCorrelationDecision.Enroll;
        if (emailMatchCount > 1)
            return IdentityCorrelationDecision.RecoveryRequired;
        return emailMatchCount == 1 && !candidateLocalOwned
            ? IdentityCorrelationDecision.Correlate
            : IdentityCorrelationDecision.Enroll;
    }

    public static bool CanCorrelate(
        AuthenticationProviderKind provider,
        string? normalizedIssuer,
        bool hasVerifiedEmail,
        IReadOnlySet<string> trustedIssuers) =>
        provider is AuthenticationProviderKind.Keycloak or AuthenticationProviderKind.Google
        && hasVerifiedEmail
        && normalizedIssuer is not null
        && trustedIssuers.Contains(normalizedIssuer);
}
