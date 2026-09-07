using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using Explore.API.Configuration;
using Explore.Persistence;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Explore.API.Scheduling;

/// <summary>
/// Applies the scheduler's ADO job-store schema. The scripts are embedded resources rather than EF Core
/// migrations because the tables are owned by Quartz.NET, not by the application's EF Core model.
/// </summary>
public sealed class QuartzSchemaInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<QuartzSchedulerSettings> options,
    ILogger<QuartzSchemaInitializer> logger)
{
    /// <summary>Token replaced with the validated table prefix before a statement is executed.</summary>
    private const string TablePrefixToken = "{prefix}";

    /// <summary>Batch separator understood by every embedded script; must sit alone on its own line.</summary>
    private const string BatchSeparator = "GO";

    private static readonly FrozenDictionary<PrimaryDatabaseProvider, string> ResourceNamesByProvider =
        new Dictionary<PrimaryDatabaseProvider, string>
        {
            [PrimaryDatabaseProvider.PostgreSql] = "QuartzSchema.PostgreSql.sql",
            [PrimaryDatabaseProvider.Sqlite] = "QuartzSchema.Sqlite.sql",
            [PrimaryDatabaseProvider.SqlServer] = "QuartzSchema.SqlServer.sql",
            [PrimaryDatabaseProvider.MariaDb] = "QuartzSchema.MySql.sql",
            [PrimaryDatabaseProvider.MySql] = "QuartzSchema.MySql.sql",
        }.ToFrozenDictionary();

    private const string ResourceNamespace = "Explore.API.Resources.Quartz.";

    public async Task ApplyAsync(PrimaryDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var schedulerOptions = options.Value;
        if (!schedulerOptions.UsePersistentStore)
        {
            logger.LogInformation(
                "Skipping Quartz scheduler schema initialization because the persistent store is not in use.");
            return;
        }

        if (!schedulerOptions.ApplySchemaOnStartup)
        {
            logger.LogInformation(
                "Skipping Quartz scheduler schema initialization because Scheduler:Quartz:ApplySchemaOnStartup is false.");
            return;
        }

        var statements = BuildStatements(provider, schedulerOptions.TablePrefix);

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>().Database;
        if (!database.IsRelational())
        {
            logger.LogInformation(
                "Skipping Quartz scheduler schema initialization because provider {ProviderName} is non-relational.",
                database.ProviderName ?? "(unknown)");
            return;
        }

        foreach (var statement in statements)
        {
            await database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        logger.LogInformation(
            "Quartz scheduler schema is present. Provider={Provider}, TablePrefix={TablePrefix}, Statements={StatementCount}",
            provider,
            schedulerOptions.TablePrefix,
            statements.Count);
    }

    /// <summary>
    /// Reads the provider script, substitutes the table prefix, and splits it into individually executable
    /// statements. Exposed so tests can assert script shape without touching a database.
    /// </summary>
    public static IReadOnlyList<string> BuildStatements(PrimaryDatabaseProvider provider, string tablePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePrefix);
        EnsureSafeTablePrefix(tablePrefix);

        var script = ReadScript(provider).Replace(TablePrefixToken, tablePrefix, StringComparison.Ordinal);

        List<string> statements = [];
        List<string> currentBatch = [];

        foreach (var line in script.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Trim().Equals(BatchSeparator, StringComparison.OrdinalIgnoreCase))
            {
                AppendBatch(statements, currentBatch);
                currentBatch.Clear();
                continue;
            }

            currentBatch.Add(trimmed);
        }

        AppendBatch(statements, currentBatch);
        return statements;
    }

    /// <summary>
    /// Drops blank and comment-only batches, and strips the leading commentary of a batch so each executed
    /// statement begins with real SQL. That keeps provider error messages and logs pointing at the statement
    /// that actually failed rather than at a block of file header comments.
    /// </summary>
    private static void AppendBatch(List<string> statements, List<string> batchLines)
    {
        var firstSqlLine = batchLines.FindIndex(IsSqlLine);
        if (firstSqlLine < 0)
        {
            return;
        }

        var batch = string.Join('\n', batchLines.Skip(firstSqlLine)).Trim();
        if (batch.Length == 0)
        {
            return;
        }

        statements.Add(batch);
    }

    private static bool IsSqlLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal);
    }

    private static string ReadScript(PrimaryDatabaseProvider provider)
    {
        if (!ResourceNamesByProvider.TryGetValue(provider, out var fileName))
        {
            throw new NotSupportedException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Quartz scheduler schema is not defined for database provider '{provider}'."));
        }

        var assembly = typeof(QuartzSchemaInitializer).GetTypeInfo().Assembly;
        var resourceName = ResourceNamespace + fileName;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Embedded Quartz scheduler schema resource '{resourceName}' was not found."));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The prefix is inlined into DDL rather than parameterized, so it must be a bare identifier fragment.
    /// <see cref="QuartzSchedulerSettingsValidator"/> enforces the same rule at startup.
    /// </summary>
    private static void EnsureSafeTablePrefix(string tablePrefix)
    {
        foreach (var character in tablePrefix)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                throw new ArgumentException(
                    "Quartz table prefix must contain only letters, digits, or underscores.",
                    nameof(tablePrefix));
            }
        }
    }
}
