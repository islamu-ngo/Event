// ABOUTME: Verifies split reporter-consent columns in the rebased PostgreSQL baseline.
// ABOUTME: Proves case-update and follow-up consent remain independent and legacy storage is absent.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
public sealed class SplitEventReportConsentMigrationTests(
    RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaseline_PersistsIndependentConsentWithoutLegacyColumn()
    {
        await ResetSharedMigrationDatabaseAsync();
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        string[] columns = await context.Database.SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'islamu_event'
                  AND table_name = 'event_reports'
                  AND column_name IN (
                      'report_case_updates_consent',
                      'report_follow_up_contact_consent',
                      'reporter_contact_consent')
                ORDER BY column_name
                """)
            .ToArrayAsync();

        await Assert.That(columns).IsEquivalentTo([
            "report_case_updates_consent",
            "report_follow_up_contact_consent"
        ]);
    }

    private ExploreDbContext CreateDbContext()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings =>
            {
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
            });
        return new ExploreDbContext(builder.Options);
    }

    private Task ResetSharedMigrationDatabaseAsync() => fixture.ResetAsync();
}
