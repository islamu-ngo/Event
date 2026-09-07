namespace Event.Architecture.Tests;

public sealed class AspireLocalInfrastructureArchitectureTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Test]
    public async Task OspreyCoordinator_MustUseReadyLocalKafkaBroker()
    {
        var appHost = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));

        await Assert.That(appHost).Contains("\"osprey-kafka\"");
        await Assert.That(appHost).Contains("OSPREY_KAFKA_BOOTSTRAP_SERVERS");
        await Assert.That(appHost).Contains("osprey-kafka:29092");
        await Assert.That(appHost).Contains("\"osprey-kafka-bootstrap\"");
        await Assert.That(appHost).Contains("WaitForCompletion(ospreyKafkaBootstrap)");
    }

    [Test]
    public async Task CerbosGrpcCompatibilityEndpoint_MustUseAspireResolvedEndpoint()
    {
        var appHost = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));

        await Assert.That(appHost).Contains(".WithEnvironment(\"CERBOS_GRPC_ENDPOINT\", cerbosGrpcEndpoint)");
    }

    [Test]
    public async Task CerbosAdminApi_MustUseMutablePostgresStore()
    {
        var cerbosConfig = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "cerbos", "config", ".cerbos.yaml"));
        var appHost = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));
        var compose = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "docker-compose.yml"));

        await Assert.That(cerbosConfig).Contains("storage:\n  driver: \"postgres\"");
        await Assert.That(appHost).Contains("\"CERBOS_PG_URL\"");
        await Assert.That(compose).Contains("CERBOS_PG_URL:");
    }

    [Test]
    public async Task MigrationWorker_MustBeRegisteredBeforeOptionalLocalDatabaseWiring()
    {
        var appHost = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));

        var registrationIndex = appHost.IndexOf("var migrations = WithProfileSecretMode(", StringComparison.Ordinal);
        await Assert.That(registrationIndex).IsGreaterThanOrEqualTo(0);

        var localDatabaseWiringIndex = appHost.IndexOf(
            "if (database is not null)",
            registrationIndex,
            StringComparison.Ordinal);
        await Assert.That(localDatabaseWiringIndex).IsGreaterThan(registrationIndex);
    }

    [Test]
    public async Task EventLocationPrivacyStage_MustBeForwardedWithoutDefault_ToMigrationWorkerAndApi()
    {
        var appHost = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));
        const string configurationRead =
            "builder.Configuration[\"Database:Migrations:EventLocationPrivacyStage\"]";
        const string environmentMapping =
            ".WithEnvironment(\"Database__Migrations__EventLocationPrivacyStage\", eventLocationPrivacyMigrationStage)";

        await Assert.That(appHost.Split(configurationRead, StringSplitOptions.None).Length - 1).IsEqualTo(1);
        await Assert.That(appHost.Split(environmentMapping, StringSplitOptions.None).Length - 1).IsEqualTo(3);
        await Assert.That(appHost).Contains("if (!string.IsNullOrWhiteSpace(eventLocationPrivacyMigrationStage))");
        await Assert.That(appHost).DoesNotContain($"{configurationRead} ??");

        int migrationWorkerIndex = appHost.IndexOf("var migrations = WithProfileSecretMode(", StringComparison.Ordinal);
        int apiIndex = appHost.IndexOf("var exploreAPI = WithProfileSecretMode(", StringComparison.Ordinal);
        int standaloneIndex = appHost.IndexOf("var eventStandalone = WithProfileSecretMode(", StringComparison.Ordinal);
        int firstMappingIndex = appHost.IndexOf(environmentMapping, StringComparison.Ordinal);
        int secondMappingIndex = appHost.IndexOf(environmentMapping, firstMappingIndex + 1, StringComparison.Ordinal);
        int thirdMappingIndex = appHost.IndexOf(environmentMapping, secondMappingIndex + 1, StringComparison.Ordinal);

        await Assert.That(firstMappingIndex).IsGreaterThan(migrationWorkerIndex);
        await Assert.That(firstMappingIndex).IsLessThan(apiIndex);
        await Assert.That(secondMappingIndex).IsGreaterThan(apiIndex);
        await Assert.That(standaloneIndex).IsGreaterThan(apiIndex);
        await Assert.That(thirdMappingIndex).IsGreaterThan(standaloneIndex);
    }

    [Test]
    public async Task WebApplications_MustExposeStableHttpsEndpoints()
    {
        var appHost = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));

        await Assert.That(appHost).Contains(".WithHttpsEndpoint(port: 7039, name: \"https\")");
        await Assert.That(appHost).Contains(".WithHttpsEndpoint(port: 7177, name: \"https\")");
    }

    [Test]
    public async Task AppHost_MustUseOnlyNamedLocalProfiles()
    {
        var launchSettings = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src", "Explore.AppHost", "Properties", "launchSettings.json"));
        using var document = System.Text.Json.JsonDocument.Parse(launchSettings);
        var profiles = document.RootElement.GetProperty("profiles");

        await Assert.That(profiles.TryGetProperty("local-lite", out _)).IsTrue();
        await Assert.That(profiles.TryGetProperty("local-core", out _)).IsTrue();
        await Assert.That(profiles.TryGetProperty("local-default", out _)).IsTrue();
        await Assert.That(profiles.TryGetProperty("local-full", out _)).IsTrue();
        await Assert.That(profiles.TryGetProperty("https", out _)).IsFalse();
    }

    [Test]
    public async Task AppHostProfiles_MustNotOverrideTheSelectedSecretAuthority()
    {
        var launchSettings = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src", "Explore.AppHost", "Properties", "launchSettings.json"));
        using var document = System.Text.Json.JsonDocument.Parse(launchSettings);

        foreach (var profile in document.RootElement.GetProperty("profiles").EnumerateObject())
        {
            var environment = profile.Value.GetProperty("environmentVariables");
            await Assert.That(environment.TryGetProperty("SecretProvider__Provider", out _)).IsFalse();
            await Assert.That(environment.TryGetProperty("SecretProvider__Infisical__ClientSecret", out _)).IsFalse();
        }
    }

    [Test]
    public async Task Formbricks_MustBeRegisteredOnlyForFullLocalMode()
    {
        string appHost = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));

        await Assert.That(appHost).Contains("if (runMode == AspireRunMode.FullLocal)\n{\n    AddLocalFormbricks(builder);\n}");
        await Assert.That(appHost).Contains("static void AddLocalFormbricks(IDistributedApplicationBuilder builder)");
        await Assert.That(appHost).Contains("\"formbricks-postgres\"");
        await Assert.That(appHost).Contains("\"formbricks-redis\"");
        await Assert.That(appHost).Contains("\"formbricks-migrate\"");
        await Assert.That(appHost).Contains("\"formbricks-hub-migrate\"");
        await Assert.That(appHost).Contains("\"formbricks-hub\"");
        await Assert.That(appHost).Contains("\"formbricks-cube\"");
        await Assert.That(appHost).Contains("\"formbricks\"");
    }

    [Test]
    public async Task PrivacyErasureAuthority_UsesTopologySpecificManagedResources()
    {
        string appHost = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));

        await Assert.That(appHost).Contains("if (usesExternalPrivacyErasureAuthority)");
        await Assert.That(appHost).DoesNotContain(
            "usesExternalPrivacyErasureAuthority && runMode == AspireRunMode.FullLocal");
        await Assert.That(appHost).Contains("PrivacyErasureAuthorityTopology.EmbeddedSqlite");
        await Assert.That(appHost).Contains("WithEmbeddedPrivacyErasureAuthority");
        await Assert.That(appHost).Contains("/app/data/privacy_erasure_authority.db");
        await Assert.That(appHost).Contains("islamu-event-privacy-erasure-authority-data");
        await Assert.That(appHost).Contains("WithLocalPrivacyErasureAuthorityDatabase");
        await Assert.That(appHost).Contains("WithExternalPrivacyErasureAuthorityDatabase");
        await Assert.That(appHost).Contains(
            "var credentialPrefix = $\"PrivacyErasureAuthorityDatabase__{role}__\";");
        await Assert.That(appHost).Contains("PrimaryDatabaseRole.Migrator");
        await Assert.That(appHost).Contains("PrimaryDatabaseRole.Runtime");
        await Assert.That(appHost).DoesNotContain("connectionName: \"PrivacyErasureAuthority");

        int blazorIndex = appHost.IndexOf("var exploreBlazor =", StringComparison.Ordinal);
        int standaloneIndex = appHost.IndexOf("var eventStandalone = WithProfileSecretMode(", StringComparison.Ordinal);
        string blazorComposition = appHost[blazorIndex..standaloneIndex];
        await Assert.That(blazorComposition).DoesNotContain("privacyErasureDatabase");
        await Assert.That(blazorComposition).DoesNotContain("PrivacyErasureAuthorityMigrator");
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx"))
                && (Directory.Exists(Path.Combine(current.FullName, "Explore.AppHost")) ||
                    Directory.Exists(Path.Combine(current.FullName, "src", "Explore.AppHost"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Explore.slnx and Explore.AppHost.");
    }
}
