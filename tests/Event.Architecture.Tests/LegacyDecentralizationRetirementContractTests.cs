using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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

    [Test]
    public async Task CurrentSchema_MustRetireLegacyDecentralizationColumnsAndSettingRows()
    {
        var snapshotType = typeof(ExploreDbContext).Assembly.GetType(
            "Explore.Persistence.Migrations.ExploreDbContextModelSnapshot", throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var properties = snapshot.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .ToArray();
        var actualAtprotoSettingKeys = AtprotoFederationSettingDefinitions.All
            .Select(definition => definition.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedAtprotoSettingKeys = CurrentAtprotoSettingKeys
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(SettingRegistry.Contains(LegacyKey)).IsFalse();
        await Assert.That(actualAtprotoSettingKeys.SequenceEqual(expectedAtprotoSettingKeys)).IsTrue();

        await Assert.That(properties.Any(property =>
            property.Name.Contains("DecentralizationEnabled", StringComparison.Ordinal))).IsFalse();
        await Assert.That(properties.Select(property => property.GetColumnName()))
            .DoesNotContain(LegacyLocalValueColumn);
        await Assert.That(properties.Select(property => property.GetColumnName()))
            .DoesNotContain(LegacyOverrideModeColumn);
    }
}
