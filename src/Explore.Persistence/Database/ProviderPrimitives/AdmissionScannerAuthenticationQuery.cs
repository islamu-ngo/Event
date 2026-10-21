using System.Data;
using System.Linq.Expressions;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class AdmissionScannerAuthenticationQuery
{
    internal static async Task<AdmissionScannerCapability?> ReadAsync(
        ExploreDbContext context,
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates,
        CancellationToken cancellationToken,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(context, enrollment, cancellationToken);
        var connection = context.Database.IsNpgsql() ? RelationalConnectionIdentity.Capture(context) : null;
        var contextId = context.ContextId;
        int? backend = connection?.BackendProcessId;
        var transaction = connection is null ? null : context.Database.CurrentTransaction?.GetDbTransaction();
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) { if (change.CurrentState != ConnectionState.Open) interrupted = true; }
        void RequireOriginal()
        {
            if (connection is not null && (interrupted || context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())))
                throw new UnauthorizedAccessException("authentication_original_connector_changed");
        }
        if (connection is not null) connection.StateChange += Changed;
        try
        {
            ParameterExpression capability = Expression.Parameter(typeof(AdmissionScannerCapability), "capability");
            Expression matches = Expression.Constant(false);
            foreach (AdmissionScannerCapabilityDigestCandidate candidate in candidates)
            {
                Expression<Func<int>> keyVersion = () => candidate.KeyVersion;
                Expression<Func<string>> lookupDigest = () => candidate.LookupDigest;
                matches = Expression.OrElse(matches, Expression.AndAlso(
                    Expression.Equal(Expression.Property(capability, nameof(AdmissionScannerCapability.LookupKeyVersion)), keyVersion.Body),
                    Expression.Equal(Expression.Property(capability, nameof(AdmissionScannerCapability.LookupDigest)), lookupDigest.Body)));
            }
            var result = await context.AdmissionScannerCapabilities.Where(Expression.Lambda<Func<AdmissionScannerCapability, bool>>(matches, capability))
                .IgnoreTenantFilter(TenantFilterBypassReasons.ScannerCapabilityAuthentication).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            RequireOriginal();
            await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            return result is not null && candidates.Any(candidate => candidate.KeyVersion == result.LookupKeyVersion
                && string.Equals(candidate.LookupDigest, result.LookupDigest, StringComparison.Ordinal)) ? result : null;
        }
        finally { if (connection is not null) connection.StateChange -= Changed; }
    }
}
