using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class PaymentReconciliationAdmission(
    IPaymentReconciliationAdmissionDbContextFactory admissionFactory) : IPaymentReconciliationAdmission
{
    public async Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimDueReconciliationsAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!acquisition.IsValidAt(observedAt) || acquisition.LeaseExpiresAt <= observedAt)
            return [];
        long startedAt = TimeProvider.System.GetTimestamp();
        bool attempted = false;
        try
        {
            await using var strategyContext = await admissionFactory.CreateDbContextAsync(cancellationToken);
            return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                bool recoverOnly = attempted;
                attempted = true;
                await using var global = await admissionFactory.CreateDbContextAsync(cancellationToken);
                DateTime observation = observedAt.Add(TimeProvider.System.GetElapsedTime(startedAt));
                return await AcquireAsync(global, acquisition, observation, recoverOnly, cancellationToken);
            });
        }
        catch
        {
            // Cancellation or a lost acknowledgement is not proof of rollback.
            // A fresh context and independent, bounded token can only recover
            // this exact live acquisition, never allocate a replacement batch.
            if (attempted)
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    IReadOnlyList<PaymentReconciliationClaim> claims = await RecoverReconciliationAcquisitionAsync(
                        acquisition, observedAt.Add(TimeProvider.System.GetElapsedTime(startedAt)), recovery.Token);
                    if (claims.Count > 0)
                        return claims;
                }
                catch
                {
                    // Preserve the acquisition failure when its outcome is still unknown.
                }
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<PaymentReconciliationClaim>> RecoverReconciliationAcquisitionAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, CancellationToken cancellationToken)
    {
        await using var global = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return await AcquireAsync(global, acquisition, observedAt, true, cancellationToken);
    }

    private static Task<IReadOnlyList<PaymentReconciliationClaim>> AcquireAsync(
        ExploreDbContext global, PaymentReconciliationAcquisition acquisition, DateTime observedAt,
        bool recoverOnly, CancellationToken cancellationToken) =>
        global.Database.IsNpgsql()
            ? PostgresPaymentReconciliationContract.ClaimAsync(
                global, acquisition, observedAt, recoverOnly, cancellationToken)
            : recoverOnly
                ? new RegistrationPaymentAttemptRepository(global).RecoverReconciliationAcquisitionAsync(
                    acquisition, observedAt, cancellationToken)
                : new RegistrationPaymentAttemptRepository(global).ClaimDueReconciliationsAsync(
                    acquisition, observedAt, cancellationToken);

    public async Task<PaymentReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt, CancellationToken cancellationToken)
    {
        await using var global = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return global.Database.IsNpgsql()
            ? await PostgresPaymentReconciliationContract.SnapshotAsync(global, observedAt, cancellationToken)
            : await new RegistrationPaymentAttemptRepository(global).GetReconciliationHealthAsync(
                observedAt, cancellationToken);
    }
}
