using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

/// <summary>
/// Reads one tenant's package moderation definition on a fresh tenantless Global context, which already
/// holds the moderation source reads used by instance export, without row fences or write privileges.
/// </summary>
internal sealed class ConfigurationManifestPackageModerationReader(
    IServiceScopeFactory scopes, IGlobalExploreDbContextFactory globalFactory,
    IConfigurationManifestPackageAuthority authority) : IConfigurationManifestPackageModerationReader
{
    public async Task<ModerationConfigurationExport> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        await authority.RequireAsync(tenantId, cancellationToken);

        ModerationConfigurationExport result;
        await using (var child = scopes.CreateAsyncScope())
        {
            var services = child.ServiceProvider;
            var context = await globalFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                services.GetRequiredService<ExploreDbContextExecutionLease>().Bind(context, null);
                if (!ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>()))
                    throw new InvalidOperationException("Package moderation export must use the selected Global context.");
            }
            catch
            {
                // Until resolved by the child, the factory lease is ours to release.
                await context.DisposeAsync();
                throw;
            }
            result = await ReadSnapshotAsync(context,
                services.GetRequiredService<ModerationConfigurationExportService>(), tenantId, cancellationToken);
        }

        // Child disposal can suspend; authority must still hold when the definition is returned.
        await authority.RequireAsync(tenantId, cancellationToken);
        return result;
    }

    /// <summary>
    /// Takes one consistent read snapshot on the Global context. Tenant import reads run inside the
    /// caller's open SQLite write transaction, where a second serializable (BEGIN IMMEDIATE) transaction
    /// would wait on the database write lock; a deferred transaction that only reads takes a WAL read
    /// snapshot instead. Other providers keep the serializable unit of work and its retry handling.
    /// </summary>
    internal static async Task<ModerationConfigurationExport> ReadSnapshotAsync(ExploreDbContext context,
        ModerationConfigurationExportService exporter, Guid tenantId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlite())
            return await exporter.ReadTenantPackageAsync(tenantId, cancellationToken);

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: true);
            await using (await context.Database.UseTransactionAsync(transaction, cancellationToken))
            {
                var result = await exporter.ReadTenantPackageInCurrentTransactionAsync(tenantId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
