namespace Event.Architecture.Tests;

public sealed class ConfigurationManifestDeploymentContractTests
{
    private static readonly string RepoRoot = ContextSystemHelpers.RepoRoot;

    [Test]
    public async Task Compose_RoutesManifestOnlyToOneShotMigrationService()
    {
        string composePath = Path.Combine(RepoRoot, "docker-compose.yml");
        string migrationService = ExtractComposeService(
            await File.ReadAllLinesAsync(composePath),
            "event-migrationservice");
        string apiService = ExtractComposeService(
            await File.ReadAllLinesAsync(composePath),
            "islamu-event-api");

        await Assert.That(migrationService).Contains("CONFIGURATION_MANIFEST_MODE");
        await Assert.That(migrationService).Contains("CONFIGURATION_MANIFEST_PATH");
        await Assert.That(migrationService).Contains("CONFIGURATION_MANIFEST_HOST_DIRECTORY");
        await Assert.That(migrationService).Contains("/etc/islamu-event/bootstrap");
        await Assert.That(migrationService).Contains("read_only: true");
        await Assert.That(apiService).DoesNotContain("CONFIGURATION_MANIFEST_");
    }

    [Test]
    public async Task Aspire_ChoosesExactlyOneManifestOwnerForEachTopology()
    {
        string source = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src", "Explore.AppHost", "AppHost.cs"));

        int splitOwner = source.IndexOf(
            "migrations = ConfigureConfigurationManifestOwner(",
            StringComparison.Ordinal);
        int standaloneOwner = source.IndexOf(
            "eventStandalone = ConfigureConfigurationManifestOwner(",
            StringComparison.Ordinal);
        int apiOwner = source.IndexOf(
            "exploreAPI = ConfigureConfigurationManifestOwner(",
            StringComparison.Ordinal);

        await Assert.That(splitOwner).IsGreaterThanOrEqualTo(0);
        await Assert.That(source[splitOwner..]).Contains(
            "hostingTopology == HostingTopology.Split");
        await Assert.That(standaloneOwner).IsGreaterThanOrEqualTo(0);
        await Assert.That(source[standaloneOwner..]).Contains(
            "hostingTopology == HostingTopology.Standalone");
        await Assert.That(apiOwner).IsEqualTo(-1);
    }

    [Test]
    public async Task ContainerImages_ExposeConventionalPathAndPublishedSchema()
    {
        string standalone = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src", "Event.Standalone", "Dockerfile"));
        string migrations = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "src", "Event.MigrationService", "Dockerfile"));

        foreach (string dockerfile in new[] { standalone, migrations })
        {
            await Assert.That(dockerfile).Contains("/etc/islamu-event/bootstrap");
            await Assert.That(dockerfile).Contains(
                "/app/schemas/configuration-manifest-v1alpha2.schema.json");
            await Assert.That(dockerfile).Contains(
                "/app/schemas/tenant-configuration-package-v1alpha2.schema.json");
            await Assert.That(dockerfile).Contains("USER $APP_UID");
        }

        await Assert.That(migrations).Contains(
            "src/Explore.Infrastructure/Explore.Infrastructure.csproj");
    }

    [Test]
    public async Task EnvironmentTemplate_DeclaresModePathAndHostMountDirectory()
    {
        string environment = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, ".env.example"));

        await Assert.That(environment).Contains("CONFIGURATION_MANIFEST_MODE=Off");
        await Assert.That(environment).Contains(
            "CONFIGURATION_MANIFEST_PATH=/etc/islamu-event/bootstrap/configuration-manifest.json");
        await Assert.That(environment).Contains(
            "CONFIGURATION_MANIFEST_HOST_DIRECTORY=./deploy/bootstrap");
    }

    private static string ExtractComposeService(
        IReadOnlyList<string> lines,
        string serviceName)
    {
        int start = -1;
        for (int index = 0; index < lines.Count; index++)
        {
            if (string.Equals(lines[index], $"  {serviceName}:", StringComparison.Ordinal))
            {
                start = index;
                break;
            }
        }

        if (start < 0)
        {
            throw new InvalidOperationException(
                $"Compose service '{serviceName}' was not found.");
        }

        int end = lines.Count;
        for (int index = start + 1; index < lines.Count; index++)
        {
            string line = lines[index];
            if (line.StartsWith("  ", StringComparison.Ordinal)
                && !line.StartsWith("    ", StringComparison.Ordinal)
                && line.EndsWith(':'))
            {
                end = index;
                break;
            }
        }

        return string.Join(Environment.NewLine, lines.Skip(start).Take(end - start));
    }
}
