using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Event.Architecture.Tests;

public sealed class LegacyDecentralizationRetirementContractTests
{
    private const string LegacyKey = "federation.decentralization_enabled";
    private const string LegacyLocalValueColumn = "tenant_delegation_decentralization_enabled_local_value";
    private const string LegacyOverrideModeColumn = "tenant_delegation_decentralization_enabled_override_mode";

    private static readonly string[] CurrentAtprotoSettingKeys =
    [
        GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
        GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
        GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
        GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode,
        GovernanceSettingKeys.Federation.AtprotoPublishMyEvents
    ];

    private static readonly string[] CurrentAtprotoSystemSettingSymbols =
    [
        "GovernanceSettingKeys.Federation.AtprotoEventsEnabled",
        "GovernanceSettingKeys.Federation.AtprotoEventValidationProfile",
        "GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled",
        "GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode"
    ];

    [Test]
    public async Task CurrentSchema_MustRetireLegacyDecentralizationColumnsAndSettingRows()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var migrationsDirectory = Path.Combine(repositoryRoot, "src", "Explore.Persistence", "Migrations");
        var snapshot = await File.ReadAllTextAsync(
            Path.Combine(migrationsDirectory, "ExploreDbContextModelSnapshot.cs"));
        var settingSeeder = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "src", "Explore.Persistence", "Seed", "LookupTableSeeder.cs"));
        var actualAtprotoSettingKeys = AtprotoFederationSettingDefinitions.All
            .Select(definition => definition.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedAtprotoSettingKeys = CurrentAtprotoSettingKeys
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(SettingRegistry.Contains(LegacyKey)).IsFalse();
        await Assert.That(actualAtprotoSettingKeys.SequenceEqual(expectedAtprotoSettingKeys)).IsTrue();

        foreach (var symbol in CurrentAtprotoSystemSettingSymbols)
        {
            await Assert.That(settingSeeder).Contains($"SettingKey = {symbol}");
        }

        await Assert.That(snapshot).DoesNotContain("DecentralizationEnabled", StringComparison.Ordinal);
        await Assert.That(snapshot).DoesNotContain(LegacyLocalValueColumn, StringComparison.Ordinal);
        await Assert.That(snapshot).DoesNotContain(LegacyOverrideModeColumn, StringComparison.Ordinal);
        await Assert.That(settingSeeder).DoesNotContain(LegacyKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the architecture test output directory.");
    }
}
