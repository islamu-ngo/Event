namespace Event.SetupAssistant.Browser.Tests;

using System.Text.Json;
using System.Xml.Linq;

public sealed class BrowserSecretBoundaryTests
{
    private static readonly string[] ForbiddenGraphTerms =
    [
        "Avalonia", "BlazorWebAssembly", "WebAssembly", "Remote.Protocol",
        "ServiceWorker", "Telemetry", "ApplicationInsights"
    ];

    [Test]
    public async Task GeneratedPublicCapabilityRemainsDisabled()
    {
        using JsonDocument document = await BrowserSecretBoundaryContract.ReadCapabilitiesAsync();
        JsonElement root = document.RootElement;

        await Assert.That(root.GetProperty("target").GetString()).IsEqualTo("browser");
        await Assert.That(root.GetProperty("targetEnabled").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("capabilities")
            .GetProperty("secretEntry").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task ApprovedDisabledBrowserExportsNoRuntimeOwner()
    {
        var contract = new BrowserSecretBoundaryContract();

        await Assert.That(contract.ExportedBrowserTypes()).IsEmpty();
        await Assert.That(contract.ReferencedAssemblies().Any(reference =>
            ForbiddenGraphTerms.Any(term =>
                reference.Contains(term, StringComparison.OrdinalIgnoreCase)))).IsFalse();
    }

    [Test]
    public async Task ApprovedDisabledBrowserHasNoRuntimeGraphOrStaticAssets()
    {
        string root = BrowserSecretBoundaryContract.RepositoryRoot();
        string projectPath = Path.Combine(
            root,
            "src",
            "Event.SetupAssistant.Browser",
            "Event.SetupAssistant.Browser.csproj");
        string lockPath = Path.Combine(
            root,
            "src",
            "Event.SetupAssistant.Browser",
            "packages.lock.json");
        XDocument project = XDocument.Load(projectPath);
        using JsonDocument lockDocument = JsonDocument.Parse(
            await File.ReadAllBytesAsync(lockPath));
        string? enabled = project.Descendants()
            .Single(element => element.Name.LocalName == "SetupTargetEnabled")
            .Value;
        string graph = lockDocument.RootElement.GetRawText();
        string sourceRoot = Path.GetDirectoryName(projectPath)!;

        await Assert.That(enabled).IsEqualTo("false");
        await Assert.That(ForbiddenGraphTerms.Any(term =>
            graph.Contains(term, StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(sourceRoot, "wwwroot"))).IsFalse();
        await Assert.That(Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))).IsEmpty();
    }
}
