namespace Event.Architecture.Tests;

public sealed class TenantSettingsBroadWriteAbsenceTests
{
    [Test]
    public async Task BroadTenantSettingsWriteMustRemainAbsent()
    {
        string root = ResolveRepositoryRoot();
        string[] retiredFiles =
        [
            "src/Explore.Application/Features/TenantOnboarding/Requests/Commands/UpdateTenantPolicySettingsCommand.cs",
            "src/Explore.Application/Features/TenantOnboarding/Handlers/Commands/UpdateTenantPolicySettingsCommandHandler.cs"
        ];

        foreach (string relativePath in retiredFiles)
        {
            await Assert.That(File.Exists(Path.Combine(root, relativePath))).IsFalse();
        }

        string controller = await File.ReadAllTextAsync(
            Path.Combine(root, "src/Explore.API/Controllers/TenantOnboardingController.cs"));
        string routeNames = await File.ReadAllTextAsync(
            Path.Combine(root, "src/Explore.API/Hateoas/RouteNames.cs"));
        string generatedClient = await File.ReadAllTextAsync(
            Path.Combine(root, "src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs"));

        await Assert.That(controller).DoesNotContain("UpdateTenantOnboardingPolicySettings");
        await Assert.That(routeNames).DoesNotContain("UpdateTenantOnboardingPolicySettings");
        await Assert.That(generatedClient).DoesNotContain("UpdateTenantOnboardingPolicySettingsAsync");
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }
}
