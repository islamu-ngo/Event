using Explore.API.Scheduling;
using Explore.Secrets.Database;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class QuartzSchemaInitializerTests
{
    /// <summary>Table names the ADO job store requires; the schema is unusable if any is missing.</summary>
    private static readonly string[] RequiredTables =
    [
        "JOB_DETAILS",
        "TRIGGERS",
        "SIMPLE_TRIGGERS",
        "CRON_TRIGGERS",
        "SIMPROP_TRIGGERS",
        "BLOB_TRIGGERS",
        "CALENDARS",
        "PAUSED_TRIGGER_GRPS",
        "FIRED_TRIGGERS",
        "SCHEDULER_STATE",
        "LOCKS"
    ];

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task BuildStatementsCoversEveryRequiredTableForEverySupportedProvider(PrimaryDatabaseProvider provider)
    {
        var statements = QuartzSchemaInitializer.BuildStatements(provider, "QRTZ_");
        var script = string.Join('\n', statements);

        await Assert.That(statements).IsNotEmpty();
        foreach (var table in RequiredTables)
        {
            await Assert.That(script.Contains("QRTZ_" + table, StringComparison.OrdinalIgnoreCase))
                .IsTrue()
                .Because($"{provider} schema must define QRTZ_{table}.");
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task BuildStatementsLeavesNoUnsubstitutedPrefixTokenAndNeverDropsData(
        PrimaryDatabaseProvider provider)
    {
        var statements = QuartzSchemaInitializer.BuildStatements(provider, "QRTZ_");

        foreach (var statement in statements)
        {
            await Assert.That(statement).DoesNotContain("{prefix}");
            // A startup-applied script must never be destructive: re-running it must not lose scheduler state.
            await Assert.That(statement.Contains("DROP ", StringComparison.OrdinalIgnoreCase)).IsFalse();
            await Assert.That(statement.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }

    /// <summary>
    /// Quartz probes for this column and silently degrades — it logs that <c>ScheduledFireTimeUtc</c> will not
    /// be corrected for misfired triggers rather than failing. Since the platform configures misfire handling
    /// explicitly, a missing column would be an invisible correctness loss, so every provider must define it.
    /// </summary>
    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task TriggersTableCarriesTheMisfireOriginalFireTimeColumn(PrimaryDatabaseProvider provider)
    {
        var script = string.Join('\n', QuartzSchemaInitializer.BuildStatements(provider, "QRTZ_"));

        await Assert.That(script.Contains("MISFIRE_ORIG_FIRE_TIME", StringComparison.OrdinalIgnoreCase))
            .IsTrue()
            .Because($"{provider} misfire handling silently loses fire-time correction without this column.");
    }

    [Test]
    public async Task BuildStatementsHonorsACustomTablePrefix()
    {
        var statements = QuartzSchemaInitializer.BuildStatements(PrimaryDatabaseProvider.Sqlite, "SCHED_");
        var script = string.Join('\n', statements);

        await Assert.That(script).Contains("SCHED_JOB_DETAILS");
        await Assert.That(script).DoesNotContain("QRTZ_");
    }

    [Test]
    public async Task BuildStatementsSplitsBatchesAndDropsCommentOnlyContent()
    {
        var statements = QuartzSchemaInitializer.BuildStatements(PrimaryDatabaseProvider.Sqlite, "QRTZ_");

        await Assert.That(statements.Count).IsGreaterThan(RequiredTables.Length);
        foreach (var statement in statements)
        {
            await Assert.That(statement.Trim()).IsNotEmpty();
            await Assert.That(statement.StartsWith("--", StringComparison.Ordinal)).IsFalse();
            await Assert.That(statement.Trim()).IsNotEqualTo("GO");
        }
    }

    [Test]
    public async Task BuildStatementsRejectsATablePrefixThatCouldInjectDdl()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            QuartzSchemaInitializer.BuildStatements(PrimaryDatabaseProvider.Sqlite, "QRTZ_; DROP TABLE users; --");
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task BuildStatementsRejectsAnEmptyTablePrefix()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            QuartzSchemaInitializer.BuildStatements(PrimaryDatabaseProvider.Sqlite, "   ");
            return Task.CompletedTask;
        });
    }
}
