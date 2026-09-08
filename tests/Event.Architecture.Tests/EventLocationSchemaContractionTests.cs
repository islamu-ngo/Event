namespace Event.Architecture.Tests;

public sealed class EventLocationSchemaContractionTests
{
    private const string ContractionSql = "location_id IS NULL OR event_location_id IS NOT NULL";
    /// <summary>Carrier configuration file and the constraint name it must declare.</summary>
    private static readonly (string ConfigurationFile, string ConstraintName)[] Carriers =
    [
        ("EventSessionConfiguration.cs", "ck_event_session_physical_location_requires_event_location"),
        ("EventSessionGroupConfiguration.cs", "ck_event_session_group_physical_location_requires_event_location"),
        ("EventAgendaItemConfiguration.cs", "ck_event_agenda_item_physical_location_requires_event_location"),
        ("EventSessionAgendaItemConfiguration.cs", "ck_event_session_agenda_item_physical_location_requires_event_location")
    ];
    /// <summary>Every provider must ship the contraction; a missing lane silently loses the guarantee.</summary>
    private static readonly (string Project, string MigrationsFolder)[] MigrationProjects =
    [
        ("Explore.Persistence", "Migrations"),
        ("Explore.Persistence.Migrations.Sqlite", "Migrations"),
        ("Explore.Persistence.Migrations.SqlServer", "Migrations"),
        ("Explore.Persistence.Migrations.MySql", "Migrations")
    ];

    [Test]
    [MethodDataSource(nameof(CarrierConfigurations))]
    public async Task EveryCarrierDeclaresThePhysicalReferenceContraction(
        string configurationFile,
        string constraintName)
    {
        string path = Path.Combine(
            ContextSystemHelpers.RepoPath("Explore.Persistence"),
            "Configurations",
            "Entities",
            configurationFile);
        string source = await File.ReadAllTextAsync(path);

        await Assert.That(source).Contains(constraintName);
        await Assert.That(source).Contains(ContractionSql);
    }

    [Test]
    [MethodDataSource(nameof(MigrationProjectNames))]
    public async Task EveryProviderShipsTheContractionMigration(string project)
    {
        string migrationsRoot = Path.Combine(ContextSystemHelpers.RepoPath(project), "Migrations");

        string[] migrations = Directory.GetFiles(migrationsRoot, "*.cs")
            .Where(path =>
                !path.EndsWith(".Designer.cs", StringComparison.Ordinal)
                && !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .ToArray();
        var matchingMigrations = new List<(string Path, string Source)>();
        foreach (string migration in migrations)
        {
            string candidate = await File.ReadAllTextAsync(migration);
            if (CountOccurrences(candidate, ContractionSql) == Carriers.Length)
            {
                matchingMigrations.Add((migration, candidate));
            }
        }

        await Assert.That(matchingMigrations).Count().IsEqualTo(1);

        (string migrationPath, string source) = matchingMigrations[0];
        await Assert.That(CountOccurrences(source, ContractionSql)).IsEqualTo(Carriers.Length);

        string designerPath = migrationPath[..^".cs".Length] + ".Designer.cs";
        await Assert.That(File.Exists(designerPath)).IsTrue();

        if (!migrationPath.EndsWith("_Init.cs", StringComparison.Ordinal))
        {
            // An incremental contraction with no reverse is not reversible in development.
            await Assert.That(CountOccurrences(source, "AddCheckConstraint")).IsEqualTo(4);
            await Assert.That(CountOccurrences(source, "DropCheckConstraint")).IsEqualTo(4);
        }
    }

    [Test]
    public async Task ContractionMigrationsAreNotHandEditedAwayFromTheGeneratedShape()
    {
        foreach ((string project, string folder) in MigrationProjects)
        {
            string migrationsRoot = Path.Combine(ContextSystemHelpers.RepoPath(project), folder);
            string[] migrationSources = Directory.GetFiles(migrationsRoot, "*.cs")
                .Where(path =>
                    !path.EndsWith(".Designer.cs", StringComparison.Ordinal)
                    && !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
                .ToArray();
            var matchingSources = new List<string>();
            foreach (string migrationSource in migrationSources)
            {
                string source = await File.ReadAllTextAsync(migrationSource);
                if (CountOccurrences(source, ContractionSql) == Carriers.Length)
                {
                    matchingSources.Add(migrationSource);
                }
            }

            // The designer snapshot is what proves the migration was produced by dotnet ef, not by hand.
            await Assert.That(matchingSources).Count().IsEqualTo(1);
            string designerPath = matchingSources[0][..^".cs".Length] + ".Designer.cs";
            await Assert.That(File.Exists(designerPath)).IsTrue();
        }
    }

    public static IEnumerable<Func<(string, string)>> CarrierConfigurations() =>
        Carriers.Select<(string ConfigurationFile, string ConstraintName), Func<(string, string)>>(
            carrier => () => (carrier.ConfigurationFile, carrier.ConstraintName));

    public static IEnumerable<Func<string>> MigrationProjectNames() =>
        MigrationProjects.Select<(string Project, string MigrationsFolder), Func<string>>(
            entry => () => entry.Project);

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
