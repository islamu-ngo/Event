using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;

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
    public async Task CurrentModelAndRegistry_RetireLegacyDecentralization()
    {
        await using var context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite("DataSource=:memory:")
            .UseSnakeCaseNamingConvention()
            .Options);
        var tables = context.Model.GetRelationalModel().Tables.ToArray();
        var columnNames = tables.SelectMany(table => table.Columns).Select(column => column.Name).ToArray();
        var propertyNames = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetFlattenedProperties())
            .Select(property => property.Name)
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

        await Assert.That(tables).IsNotEmpty();
        await Assert.That(propertyNames.Where(name => name.Contains("DecentralizationEnabled", StringComparison.Ordinal))).IsEmpty();
        await Assert.That(columnNames).DoesNotContain(LegacyLocalValueColumn);
        await Assert.That(columnNames).DoesNotContain(LegacyOverrideModeColumn);
    }
}
