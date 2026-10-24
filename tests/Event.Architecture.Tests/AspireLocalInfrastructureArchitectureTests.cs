namespace Event.Architecture.Tests;

using YamlDotNet.RepresentationModel;

public sealed class AspireLocalInfrastructureArchitectureTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Test]
    public async Task CerbosAdminApi_MustUseMutablePostgresStore()
    {
        var cerbosConfig = ReadYamlMapping(Path.Combine(RepoRoot, "cerbos", "config", ".cerbos.yaml"));
        var compose = ReadYamlMapping(Path.Combine(RepoRoot, "docker-compose.yml"));
        var storage = (YamlMappingNode)cerbosConfig.Children[new YamlScalarNode("storage")];
        var services = (YamlMappingNode)compose.Children[new YamlScalarNode("services")];
        var cerbos = (YamlMappingNode)services.Children[new YamlScalarNode("cerbos")];
        var environment = (YamlMappingNode)cerbos.Children[new YamlScalarNode("environment")];

        await Assert.That(((YamlScalarNode)storage.Children[new YamlScalarNode("driver")]).Value)
            .IsEqualTo("postgres");
        await Assert.That(environment.Children.TryGetValue(new YamlScalarNode("CERBOS_PG_URL"), out var connection))
            .IsTrue();
        await Assert.That(connection is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value)).IsTrue();
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
            var keys = environment.EnumerateObject()
                .Select(property => property.Name.Replace("__", ":", StringComparison.Ordinal))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            await Assert.That(keys.Contains("SecretProvider:Provider")).IsFalse();
            await Assert.That(keys.Contains("SecretProvider:Infisical:ClientSecret")).IsFalse();
        }
    }

    private static YamlMappingNode ReadYamlMapping(string path)
    {
        using var reader = File.OpenText(path);
        var document = new YamlStream();
        document.Load(reader);
        return (YamlMappingNode)document.Documents.Single().RootNode;
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
