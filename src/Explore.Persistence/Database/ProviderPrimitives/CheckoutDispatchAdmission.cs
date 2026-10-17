using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class CheckoutDispatchAdmission(ICheckoutDispatchAdmissionDbContextFactory factory)
    : ICheckoutDispatchAdmission
{
    public async Task<IReadOnlyList<CheckoutDispatchClaim>> ClaimDueDispatchEffectsAsync(
        string leaseOwner, int batchSize, DateTime claimedAt, TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Trim().Length > CheckoutDispatchEffect.MaxLeaseOwnerLength ||
            batchSize is < 1 or > 1000 || claimedAt.Kind != DateTimeKind.Utc || leaseDuration <= TimeSpan.Zero)
            return [];
        await using var global = await factory.CreateDbContextAsync(cancellationToken);
        if (global.BindTenantLease() is not null || global.IsTenantFilterBypassed || global.CurrentUserService is not null)
            throw new InvalidOperationException("checkout_admission_requires_unbound_global_lease");
        return global.Database.IsNpgsql()
            ? await PostgresCheckoutDispatchContract.ClaimAsync(
                global, leaseOwner.Trim(), claimedAt, claimedAt.Add(leaseDuration), cancellationToken)
            : await new RegistrationPaymentAttemptRepository(global).ClaimDueDispatchEffectsAsync(
                leaseOwner, batchSize, claimedAt, leaseDuration, cancellationToken);
    }
}
