using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class MigrationLedgerExtractorTests
{
    private const string PostgresSnapshotPath = "src/Explore.Persistence/Migrations/ExploreDbContextModelSnapshot.cs";
    private const string PostgresInitMigrationPath = "src/Explore.Persistence/Migrations/20260929055408_Init.cs";
    private const string InitStubComment = "// init";
    private const string DesignerStubComment = "// designer";

    private static readonly string BaseOid = new('a', 40);
    private static readonly string HeadOid = new('b', 40);

    [Test]
    public async Task DiscoversSnapshotsAndMigrationsDynamicallyExcludingDesignerAndSnapshotFiles()
    {
        var baseFiles = new Dictionary<string, string>
        {
            [PostgresSnapshotPath] = "// snapshot v1",
            [PostgresInitMigrationPath] = InitStubComment,
            ["src/Explore.Persistence/Migrations/20260929055408_Init.Designer.cs"] = DesignerStubComment,
            ["src/Explore.Persistence.Migrations.Sqlite/Migrations/ExploreDbContextModelSnapshot.cs"] = "// sqlite snapshot v1",
            ["src/Explore.Persistence.Migrations.Sqlite/Migrations/20260929055409_Init.cs"] = InitStubComment,
            ["src/Explore.Persistence.Migrations.Sqlite/Migrations/20260929055409_Init.Designer.cs"] = DesignerStubComment,
        };

        var headFiles = new Dictionary<string, string>(baseFiles)
        {
            [PostgresSnapshotPath] = "// snapshot v2",
            ["src/Explore.Persistence/Migrations/20261124120000_AddEventTags.cs"] = "// add tags",
            ["src/Explore.Persistence/Migrations/20261124120000_AddEventTags.Designer.cs"] = DesignerStubComment,
            ["src/Explore.Persistence.Migrations.Sqlite/Migrations/ExploreDbContextModelSnapshot.cs"] = "// sqlite snapshot v2",
            ["src/Explore.Persistence.Migrations.Sqlite/Migrations/20261124120001_AddEventTags.cs"] = "// add tags",
            ["src/Explore.Persistence.Migrations.Sqlite/Migrations/20261124120001_AddEventTags.Designer.cs"] = DesignerStubComment,
        };

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, baseFiles)
            .AddCommit(HeadOid, headFiles);

        ContractDifferResult result = new MigrationLedgerExtractor().Diff(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "migrations");
        await Assert.That(result.MigrationLedger).IsNotNull();
        await Assert.That(result.MigrationLedger!.AddedMigrations).Count().IsEqualTo(2);
        await Assert.That(result.MigrationLedger.AddedMigrations.All(id => !id.EndsWith(".Designer", StringComparison.Ordinal) && !id.Contains("ModelSnapshot", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.MigrationLedger.RemovedMigrations).IsEmpty();
        await Assert.That(result.MigrationLedger.AsymmetricContexts).IsEmpty();
        await Assert.That(result.Changes.Count(c => c.Kind == "migration_added" && c.Severity == ContractChangeSeverity.Additive)).IsEqualTo(2);
    }

    [Test]
    public async Task ReportsRemovedMigrationsAsBreakingAndAsymmetricProviderCoverageAsReviewRequiredWithoutClaimingParity()
    {
        var baseFiles = new Dictionary<string, string>
        {
            [PostgresSnapshotPath] = "// pg v1",
            [PostgresInitMigrationPath] = InitStubComment,
            ["src/Explore.Persistence/Migrations/20261001010101_OldStep.cs"] = "// old",
            ["src/Explore.Persistence.Migrations.MySql/Migrations/ExploreDbContextModelSnapshot.cs"] = "// mysql v1",
            ["src/Explore.Persistence.Migrations.MySql/Migrations/20260929055408_Init.cs"] = InitStubComment,
            ["src/Explore.Persistence.Migrations.MySql/Migrations/20261001010101_OldStep.cs"] = "// old",
        };

        var headFiles = new Dictionary<string, string>
        {
            [PostgresSnapshotPath] = "// pg v2",
            [PostgresInitMigrationPath] = InitStubComment,
            ["src/Explore.Persistence/Migrations/20261124120000_PostgresOnlyFeature.cs"] = "// new",
            ["src/Explore.Persistence.Migrations.MySql/Migrations/ExploreDbContextModelSnapshot.cs"] = "// mysql v1",
            ["src/Explore.Persistence.Migrations.MySql/Migrations/20260929055408_Init.cs"] = InitStubComment,
            ["src/Explore.Persistence.Migrations.MySql/Migrations/20261001010101_OldStep.cs"] = "// old",
        };

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, baseFiles)
            .AddCommit(HeadOid, headFiles);

        ContractDifferResult result = new MigrationLedgerExtractor().Diff(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "asymmetry");
        await Assert.That(result.Changes.Any(c => c.Kind == "migration_removed" && c.Severity == ContractChangeSeverity.Breaking)).IsTrue();
        await Assert.That(result.MigrationLedger!.AsymmetricContexts).Contains("ExploreDbContext");
        ContractDiffChange asymmetry = result.Changes.Single(c => c.Kind == "migration_provider_asymmetry");
        await Assert.That(asymmetry.Severity).IsEqualTo(ContractChangeSeverity.ReviewRequired);
        await Assert.That(asymmetry.Summary).DoesNotContain("parity proven");
        await Assert.That(asymmetry.Summary).DoesNotContain("semantically equivalent");
    }

    [Test]
    public async Task SnapshotChangeWithoutNewMigrationIsFlaggedAsReviewRequired()
    {
        var baseFiles = new Dictionary<string, string>
        {
            [PostgresSnapshotPath] = "// pg v1",
            [PostgresInitMigrationPath] = InitStubComment,
        };
        var headFiles = new Dictionary<string, string>
        {
            [PostgresSnapshotPath] = "// pg modified directly",
            [PostgresInitMigrationPath] = InitStubComment,
        };

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, baseFiles)
            .AddCommit(HeadOid, headFiles);

        ContractDifferResult result = new MigrationLedgerExtractor().Diff(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "migration_snapshot_modified_without_migration" && c.Severity == ContractChangeSeverity.ReviewRequired)).IsTrue();
    }
}
