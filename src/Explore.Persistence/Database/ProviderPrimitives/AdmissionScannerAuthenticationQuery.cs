using System.Linq.Expressions;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class AdmissionScannerAuthenticationQuery
{
    internal static async Task<AdmissionScannerCapability?> ReadAsync(
        ExploreDbContext context,
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates,
        CancellationToken cancellationToken)
    {
        IQueryable<AdmissionScannerCapability> capabilities;
        if (context.Database.IsNpgsql())
        {
            string command = $$"""
                SELECT * FROM {{PostgresAdmissionScannerAuthenticationContract.FunctionSql(context)}}({0}, {1})
                """;
            capabilities = context.AdmissionScannerCapabilities.FromSqlRaw(
                command,
                candidates.Select(candidate => candidate.KeyVersion).ToArray(),
                candidates.Select(candidate => candidate.LookupDigest).ToArray());
        }
        else
        {
            ParameterExpression capability = Expression.Parameter(typeof(AdmissionScannerCapability), "capability");
            Expression matches = Expression.Constant(false);
            foreach (AdmissionScannerCapabilityDigestCandidate candidate in candidates)
            {
                Expression<Func<int>> keyVersion = () => candidate.KeyVersion;
                Expression<Func<string>> lookupDigest = () => candidate.LookupDigest;
                matches = Expression.OrElse(matches, Expression.AndAlso(
                    Expression.Equal(
                        Expression.Property(capability, nameof(AdmissionScannerCapability.LookupKeyVersion)),
                        keyVersion.Body),
                    Expression.Equal(
                        Expression.Property(capability, nameof(AdmissionScannerCapability.LookupDigest)),
                        lookupDigest.Body)));
            }
            capabilities = context.AdmissionScannerCapabilities.Where(
                Expression.Lambda<Func<AdmissionScannerCapability, bool>>(matches, capability));
        }

        return await capabilities
            .IgnoreTenantFilter(TenantFilterBypassReasons.ScannerCapabilityAuthentication)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }
}
