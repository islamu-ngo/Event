namespace Event.SetupAssistant.Browser.Tests;

using System.Text.Json;

public sealed class BrowserReleaseContractTests
{
    [Test]
    public async Task ApprovedDisabledTargetHasNoPublishableBrowserSurface()
    {
        string root = BrowserSecretBoundaryContract.RepositoryRoot();
        string sourceRoot = Path.Combine(root, "src", "Event.SetupAssistant.Browser");
        using JsonDocument capabilities =
            await BrowserSecretBoundaryContract.ReadCapabilitiesAsync();
        string[] forbiddenFiles =
        [
            "index.html", "service-worker.js", "manifest.webmanifest",
            "appsettings.json", "web.config", "staticwebapp.config.json"
        ];
        string[] present = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .Where(path => forbiddenFiles.Contains(
                Path.GetFileName(path),
                StringComparer.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(capabilities.RootElement.GetProperty("targetEnabled")
            .GetBoolean()).IsFalse();
        await Assert.That(capabilities.RootElement.GetProperty("capabilities")
            .GetProperty("secretEntry").GetBoolean()).IsFalse();
        await Assert.That(present).IsEmpty();
    }
}
