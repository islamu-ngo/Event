using Event.Persistence.IntegrationTests.Database;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Event.Persistence.IntegrationTests;

public sealed class SqlServerEvidenceDeleteHistoryTests
{
    private const string EvidenceForeignKey = "fk_moderation_case_evidence_moderation_cases_case_id";
    private const string PiiForeignKey = "fk_user_birth_date_collection_receipts_user_pii_user_id";

    [Test]
    [Arguments("20261019151338_PreservePrivateBirthDatePiiCascade")]
    [Arguments("20261020181307_ImmutableRegistrationProviderBindingPublication")]
    [Arguments("20261022194317_AddAdjudicatedCaseReadGrants")]
    public async Task Evidence_rollback_preserves_non_cascading_predecessor_without_mutating_history(string id)
    {
        using var context = CreateContext();
        var migration = ReadMigration(context, id);
        var operation = migration.DownOperations.OfType<AddForeignKeyOperation>()
            .Single(key => key.Name == EvidenceForeignKey);
        await Assert.That(operation.OnDelete).IsEqualTo(ReferentialAction.Cascade);

        string first = Generate(context, operation);
        string second = Generate(context, operation);

        await Assert.That(first).Contains("REFERENCES [islamu_event].[moderation_cases]");
        await Assert.That(first.Contains("ON DELETE CASCADE", StringComparison.Ordinal)).IsFalse();
        await Assert.That(first).IsEqualTo(second);
        await Assert.That(operation.OnDelete).IsEqualTo(ReferentialAction.Cascade);
    }

    [Test]
    public async Task Private_birth_date_receipt_still_cascades_with_its_pii_parent()
    {
        using var context = CreateContext();
        var migration = ReadMigration(context, "20261019151338_PreservePrivateBirthDatePiiCascade");
        var operation = migration.UpOperations.OfType<AddForeignKeyOperation>()
            .Single(key => key.Name == PiiForeignKey);

        string script = Generate(context, operation);

        await Assert.That(script).Contains("REFERENCES [islamu_event].[user_pii]");
        await Assert.That(script).Contains("ON DELETE CASCADE");
        await Assert.That(operation.OnDelete).IsEqualTo(ReferentialAction.Cascade);
    }

    private static ExploreDbContext CreateContext()
    {
        using var provider = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var options = TestDbContextOptions.Create((DbContextOptions<ExploreDbContext>)provider.GetService<IDbContextOptions>());
        options.UseSqlServer(provider.Database.GetConnectionString(),
            sql => sql.MigrationsAssembly("Explore.Persistence.Migrations.SqlServer"));
        options.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqlServerMigrationsSqlGenerator>();
        return new ExploreDbContext(options.Options);
    }

    private static Migration ReadMigration(ExploreDbContext context, string id)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        return assembly.CreateMigration(assembly.Migrations[id], context.Database.ProviderName!);
    }

    private static string Generate(ExploreDbContext context, AddForeignKeyOperation operation) =>
        string.Join('\n', context.GetService<IMigrationsSqlGenerator>().Generate([operation])
            .Select(command => command.CommandText));
}
