using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Authentication.Continuation;

public sealed class ResolveVerifiedAccountContinuationQueryHandler(
    IExternalLoginAuthenticationRepository logins,
    IUserTenantNavigationRepository navigation,
    IUserTenantMembershipRepository memberships)
    : IQueryHandler<ResolveVerifiedAccountContinuationQuery, VerifiedAccountContinuationResult>
{
    public async Task<VerifiedAccountContinuationResult> QueryAsync(
        ResolveVerifiedAccountContinuationQuery request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderAccountKey account = request.AccountKey;
        if (account is null || request.TargetTenantId == Guid.Empty
            || account.ProviderKind is not (AuthenticationProviderKind.Local
                or AuthenticationProviderKind.Keycloak or AuthenticationProviderKind.Atproto))
            return VerifiedAccountContinuationResult.Unavailable();

        Guid localSubject = Guid.Empty;
        if (account.ProviderKind == AuthenticationProviderKind.Local
            && (!Guid.TryParseExact(account.Value, "D", out localSubject) || localSubject == Guid.Empty
                || !string.Equals(account.Value, localSubject.ToString("D"), StringComparison.Ordinal)))
            return VerifiedAccountContinuationResult.Unavailable();
        if (account.ProviderKind == AuthenticationProviderKind.Atproto
            && !AtprotoDid.TryParse(account.Value, out _))
            return VerifiedAccountContinuationResult.Unavailable();

        try
        {
            var login = await logins.GetByProviderAndKey(account, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (login is null)
                return VerifiedAccountContinuationResult.Unlinked();
            // Database collations can be broader than the provider's ordinal account identity.
            if (login.Id == Guid.Empty || login.UserId == Guid.Empty
                || login.AuthenticationProviderId != (int)account.ProviderKind
                || !string.Equals(login.ProviderKey, account.Value, StringComparison.Ordinal)
                || account.ProviderKind == AuthenticationProviderKind.Local && login.UserId != localSubject)
                return VerifiedAccountContinuationResult.Unavailable();

            var user = await navigation.GetNavigationUserAsync(login.UserId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (user is null || user.IsDeleted || user.Id != login.UserId)
                return VerifiedAccountContinuationResult.Unavailable();

            var rows = await memberships.GetActiveTenantsForUserAsync(user.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var active = rows.Where(member =>
                member.UserId == user.Id && !member.IsDeleted
                && member.StatusId == (int)TenantUserStatusEnum.Active
                && member.TenantId != Guid.Empty
                && member.Tenant is { } tenant
                && tenant.TenantStatusId == (int)TenantStatusEnum.Active
                && tenant.Id == member.TenantId
                && !string.IsNullOrWhiteSpace(tenant.Slug)).ToList();

            var selected = request.TargetTenantId is { } target
                ? active.FirstOrDefault(member => member.TenantId == target)
                : active.FirstOrDefault(member => member.TenantId == user.LastActiveTenantId)
                    ?? active.OrderByDescending(member => member.JoinedAt ?? member.CreatedAt).FirstOrDefault();
            return selected is null
                ? VerifiedAccountContinuationResult.NoMembership()
                : VerifiedAccountContinuationResult.Destination(
                    selected.TenantId, selected.Tenant.Slug, active.Count > 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return VerifiedAccountContinuationResult.Unavailable();
        }
    }
}
