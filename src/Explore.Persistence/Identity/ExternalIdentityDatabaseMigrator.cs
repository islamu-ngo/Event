using Explore.Secrets.Database;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Explore.Persistence.Identity;

public static class ExternalIdentityDatabaseMigrator
{
    public static async Task<bool> MigrateIfExternalAsync(
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        if (IdentityDatabaseConfiguration.GetTopology(configuration)
            != IdentityDatabaseTopology.External)
        {
            return false;
        }

        var database = IdentityDatabaseConfiguration.BindExternal(configuration, PrimaryDatabaseRole.Migrator);
        var writerEnrollment = database.Provider == PrimaryDatabaseProvider.PostgreSql
            ? BindLocalIdentityWriterEnrollment(configuration) : null;
        var options = new DbContextOptionsBuilder<ExternalIdentityDbContext>();
        IdentityDatabaseProviderComposition.Configure(
            options,
            database);
        await using var context = new ExternalIdentityDbContext(options.Options);

        logger.LogInformation(
            "Applying external Local Identity database migrations.");
        await MigrateCoreAsync(context, writerEnrollment, cancellationToken);
        return true;
    }

    /// <summary>Applies external identity storage and its declared writer permissions on the caller's migration context.</summary>
    public static async Task MigrateAsync(
        ExternalIdentityDbContext context,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);

        var writerEnrollment = context.Database.IsNpgsql()
            ? BindLocalIdentityWriterEnrollment(configuration) : null;
        await MigrateCoreAsync(context, writerEnrollment, cancellationToken);
    }

    private static async Task MigrateCoreAsync(
        ExternalIdentityDbContext context, PostgresLocalIdentityWriterEnrollment? writerEnrollment,
        CancellationToken cancellationToken)
    {
        await context.Database.MigrateAsync(cancellationToken);
        if (writerEnrollment is not null)
        {
            await PostgresLocalIdentityWriterPermissions.ApplyAsync(context, writerEnrollment,
                IdentityDatabaseTopology.External, LocalIdentityWriterInventory.Store.Identity,
                cancellationToken);
        }
    }

    private static PostgresLocalIdentityWriterEnrollment? BindLocalIdentityWriterEnrollment(
        IConfiguration configuration)
    {
        if (!IdentityDatabaseConfiguration.TryBindUsername(configuration,
                PrimaryDatabaseRole.LocalIdentityWriter, out string? writerLogin))
            return null;

        var enrollment = new PostgresLocalIdentityWriterEnrollment(
            IdentityDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Migrator),
            IdentityDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Runtime),
            writerLogin!);
        enrollment.Validate();
        return enrollment;
    }
}
