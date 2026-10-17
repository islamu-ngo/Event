using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal sealed class InstanceApiKeyCapabilityQueries(ExploreDbContext context)
{
    private readonly InstanceApiKeyCapabilitySql _sql = new(context);

    internal Task<bool> ScalarAsync(string function, string arguments, object[] parameters, CancellationToken token) =>
        context.Database.SqlQueryRaw<bool>(
                $"SELECT {_sql.Function(function)}({arguments}) AS \"Value\"", parameters).SingleAsync(token);

    internal Task<List<ExternalApiKey>> ReadAsync(string function, object[] parameters, CancellationToken token) =>
        context.ExternalApiKeys.FromSqlRaw(
                        $"SELECT * FROM {_sql.Function(function)}(@actor, @provider, @account, @key)",
                        parameters)
                    .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement)
                    .AsNoTracking().ToListAsync(token);

    internal Task<ExternalApiKeyIssuanceReceipt?> ReceiptAsync(object[] parameters, CancellationToken token) =>
        context.ExternalApiKeyIssuanceReceipts.FromSqlRaw(
                        $"SELECT * FROM {_sql.Function(PostgresInstanceApiKeyIssuanceContract.ReceiptFunction)}(@actor, @provider, @account, @operation)",
                        parameters)
                    .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement)
                    .AsNoTracking().SingleOrDefaultAsync(token);

    internal Task<ExternalApiKey?> RecoverAsync(object[] parameters, CancellationToken token) =>
        context.ExternalApiKeys.FromSqlRaw(
                        $"SELECT * FROM {_sql.Function(PostgresInstanceApiKeyIssuanceContract.RecoveryFunction)}(@actor, @provider, @account, @operation, @key)",
                        parameters)
                    .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement)
                    .AsNoTracking().SingleOrDefaultAsync(token);

    internal Task<List<TenantApiKeyUsageSummary>> ReportAsync(object[] parameters, CancellationToken token) =>
        context.Database.SqlQueryRaw<TenantApiKeyUsageSummary>(
                        $"SELECT * FROM {_sql.Function(PostgresPlatformApiKeyUsageReportContract.ReportFunction)}(@actor, @provider, @account, @from, @to)",
                        parameters)
                    .ToListAsync(token);
}
