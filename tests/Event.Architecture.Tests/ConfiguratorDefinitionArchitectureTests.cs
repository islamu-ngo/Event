namespace Event.Architecture.Tests;

using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

public sealed class ConfiguratorDefinitionArchitectureTests
{
    [Test]
    public async Task DefinitionReceiptBindsEventOwnedMetadataAndCanonicalBytes()
    {
        string directory = ContextSystemHelpers.RepoPath(
            "configurator", "definitions", "islamu-event");
        using JsonDocument receipt = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(directory, "release.json")));
        using JsonDocument definition = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(directory, "definition.json")));
        JsonElement identity = definition.RootElement;
        JsonElement release = receipt.RootElement;

        await Assert.That(release.GetProperty("generatedBy").GetString())
            .IsEqualTo("eng/configurator/DefinitionExporter");
        await Assert.That(release.GetProperty("familyId").GetString())
            .IsEqualTo(identity.GetProperty("familyId").GetString());
        await Assert.That(release.GetProperty("revision").GetInt64())
            .IsEqualTo(identity.GetProperty("revision").GetInt64());
        foreach (string file in new[] { "definition.json", "setup-definition.json", "public-definition.json" })
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(directory, file));
            string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await Assert.That(digest).IsEqualTo(release.GetProperty("definitionDigest").GetString());
        }
    }

    [Test]
    public async Task EventClientOwnsSetupLiveTransportWithoutSetupEngineDependency()
    {
        var adapter = typeof(Explore.Blazor.Client.Services.SetupLive.SetupLiveAccessToken);
        await Assert.That(adapter.Assembly.GetName().Name).IsEqualTo("Explore.Blazor.Client");
        XDocument client = XDocument.Load(ContextSystemHelpers.RepoPath(
            "src", "Explore.Blazor.Client", "Explore.Blazor.Client.csproj"));
        string[] forbidden = client.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(path => path.Contains("Event.Setup", StringComparison.Ordinal)
                || path.Contains("Configurator.", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(forbidden).IsEmpty();
    }
}
