// ABOUTME: Routes lifecycle email using the exact persisted provider-account binding of the target user.
// ABOUTME: Keeps Local delivery and external authorities independent of deployment-wide SMTP defaults.

using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class DefaultAccountAuthorityLifecycleEmailService(
    IUserExternalLoginRepository externalLogins,
    IEnumerable<IAccountAuthorityLifecycleEmailProvider> providers) : IAccountAuthorityLifecycleEmailService
{
    public Task<AccountAuthorityLifecycleEmailResult> RequestEmailVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default) =>
        RequestAsync(AccountAuthorityLifecycleEmailAction.EmailVerification, request, cancellationToken);

    public Task<AccountAuthorityLifecycleEmailResult> RequestPasswordResetAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default) =>
        RequestAsync(AccountAuthorityLifecycleEmailAction.PasswordReset, request, cancellationToken);

    public Task<AccountAuthorityLifecycleEmailResult> RequestEmailUpdateVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default) =>
        RequestAsync(AccountAuthorityLifecycleEmailAction.EmailUpdateVerification, request, cancellationToken);

    private async Task<AccountAuthorityLifecycleEmailResult> RequestAsync(
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var login = await externalLogins.GetById(request.ExternalLoginId);
        if (login is null || login.UserId != request.UserId)
        {
            return new(AccountAuthorityLifecycleEmailStatus.AccountNotLinked, action, AccountAuthorityKind.None,
                ReasonCode: "account_authority_account_not_linked");
        }

        var kind = (AuthenticationProviderKind)login.AuthenticationProviderId switch
        {
            AuthenticationProviderKind.Local => AccountAuthorityKind.LocalIdentity,
            AuthenticationProviderKind.Keycloak => AccountAuthorityKind.Keycloak,
            AuthenticationProviderKind.Atproto => AccountAuthorityKind.AtprotoPds,
            AuthenticationProviderKind.Google => AccountAuthorityKind.ExternalOidc,
            _ => AccountAuthorityKind.None
        };
        if (kind == AccountAuthorityKind.None)
        {
            return new(AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured, action, kind,
                ReasonCode: "account_authority_provider_not_configured");
        }

        var authority = new ResolvedAccountAuthority(login.Id, login.UserId,
            new ProviderAccountKey((AuthenticationProviderKind)login.AuthenticationProviderId, login.ProviderKey), kind);
        // ATProto/PDS and other OIDC lifecycle actions stay with their native account provider.
        if (kind is AccountAuthorityKind.AtprotoPds or AccountAuthorityKind.ExternalOidc)
        {
            return new(AccountAuthorityLifecycleEmailStatus.ProviderManaged, action, kind,
                ReasonCode: "account_authority_provider_managed");
        }

        var provider = providers.SingleOrDefault(provider => provider.Kind == kind);
        return provider is null
            ? new(AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured, action, kind,
                ReasonCode: "account_authority_provider_not_configured")
            : await provider.RequestAsync(action, request, authority, cancellationToken);
    }
}
