using ISLAMU.Wire.Contracts.Identity;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Identity;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services;

public sealed class IdentityAccountResolver(
    IUserRepository users,
    IUserExternalLoginRepository logins,
    IUserIdentityEmailRepository identityEmails,
    IPrivacyErasureStateRepository privacyErasure,
    IOptions<IdentityCorrelationOptions> options) : IIdentityAccountResolver
{
    private readonly HashSet<string> _trustedIssuers = options.Value.TrustedIssuers
        .Select(OidcIssuerAuthority.Normalize).ToHashSet(StringComparer.Ordinal);

    public async Task<IdentityAccountResolution> ResolveAsync(
        ProviderAccountKey accountKey,
        IdentityAuthorityEvidence? evidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UserExternalLogin? binding = await logins.GetByProviderAndKey(accountKey);
        cancellationToken.ThrowIfCancellationRequested();
        bool conflictingEvidence = evidence is not null && evidence.AccountKey != accountKey;
        User? user = binding is null ? null : await users.GetById(binding.UserId);
        bool bindingConflict = conflictingEvidence || (binding is not null && user is null);
        string? issuer = evidence?.Issuer;
        bool verifiedEmail = evidence is { EmailVerified: true }
            && !string.IsNullOrWhiteSpace(evidence.Email);
        bool canClaim = !conflictingEvidence && IdentityCorrelationPolicy.CanCorrelate(
            accountKey.ProviderKind, issuer, verifiedEmail, _trustedIssuers);
        int matches = 0;
        bool localOwned = false;

        if (binding is null && !bindingConflict && canClaim)
        {
            UserIdentityEmailClaim? claim = await identityEmails.GetByNormalizedEmailAsync(
                evidence!.Email, cancellationToken);
            if (claim is not null)
            {
                matches = 1;
                List<UserExternalLogin> candidateBindings = await logins.GetByUser(claim.UserId);
                localOwned = candidateBindings.Any(login =>
                    login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local);
                user = await users.GetById(claim.UserId);
                IReadOnlyList<UserIdentityEmailClaim> supported = await identityEmails.GetByUserAsync(
                    claim.UserId, cancellationToken);
                bindingConflict = localOwned || user is null || !supported.Any(value => value.Id == claim.Id);
            }
        }

        if (user is not null && await privacyErasure.GetBySubjectAsync(user.Id, cancellationToken) is not null)
            bindingConflict = true;
        cancellationToken.ThrowIfCancellationRequested();
        IdentityCorrelationDecision decision = IdentityCorrelationPolicy.Evaluate(
            binding is not null, bindingConflict, accountKey.ProviderKind,
            issuer, verifiedEmail, _trustedIssuers, matches, localOwned);
        return new IdentityAccountResolution(decision, user, canClaim);
    }
}
