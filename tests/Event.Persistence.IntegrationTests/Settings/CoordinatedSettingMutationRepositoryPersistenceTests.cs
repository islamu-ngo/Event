namespace Event.Persistence.IntegrationTests.Settings;

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class CoordinatedSettingMutationRepositoryPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SeedCreatesCanonicalGuardedPolicyRows()
    {
        await fixture.ResetAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            await seed.SystemSettings.Where(row => GuardedKeys.Contains(row.SettingKey)).ExecuteDeleteAsync();
            await LookupTableSeeder.SeedAsync(seed);
        }
        Guid[] ids =
        [
            SeedIds.SystemSettingEventReportingIntakeEnabledId, SeedIds.SystemSettingRequireApprovalId,
            SeedIds.SystemSettingUserSubmissionEnabledId, SeedIds.SystemSettingOrgSubmissionEnabledId,
            SeedIds.SystemSettingGroupSubmissionEnabledId
        ];
        string[] values = ["true", "false", "true", "true", "true"];
        await using var observer = fixture.CreateDbContext();
        var rows = await observer.SystemSettings.AsNoTracking()
            .Where(row => GuardedKeys.Contains(row.SettingKey)).ToArrayAsync();
        await Assert.That(rows.Length).IsEqualTo(5);
        for (int index = 0; index < GuardedKeys.Length; index++)
        {
            var row = rows.Single(item => item.SettingKey == GuardedKeys[index]);
            await Assert.That(row.Id).IsEqualTo(ids[index]);
            await Assert.That(row.Value).IsEqualTo(values[index]);
            await Assert.That(row.ValueType).IsEqualTo(SettingValueType.Boolean);
            await Assert.That(row.IsLocked).IsFalse();
            await Assert.That(row.CreatedBy).IsNull();
            await Assert.That(row.UpdatedBy).IsNull();
            await Assert.That(row.UpdatedAt).IsNull();
        }
    }

    [Test]
    public async Task LookupSeedMaterializesPrimaryReportingIntakeSystemSettingAndDefinitionSemantics()
    {
        await fixture.ResetAsync();
        await using (ExploreDbContext resetContext = fixture.CreateDbContext())
        {
            await resetContext.SystemSettings
                .Where(row => row.SettingKey == GuardedKeys[0])
                .ExecuteDeleteAsync();
            await LookupTableSeeder.SeedAsync(resetContext);
        }

        await using ExploreDbContext context = fixture.CreateDbContext();
        SystemSetting setting = await context.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GuardedKeys[0]);
        SettingDefinition definition = EventReportingIntakeSettingDefinitions.IntakeEnabled;
        Guid unusedDeterministicId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000554");

        await Assert.That(SeedIds.SystemSettingEventReportingIntakeEnabledId)
            .IsEqualTo(unusedDeterministicId);
        await Assert.That(setting.Id).IsEqualTo(unusedDeterministicId);
        await Assert.That(setting.Value).IsEqualTo("true");
        await Assert.That(setting.ValueType).IsEqualTo(SettingValueType.Boolean);
        await Assert.That(setting.IsLocked).IsFalse();
        await Assert.That(definition.DefaultValue).IsEqualTo("true");
        await Assert.That(definition.IsSensitive).IsFalse();
        await Assert.That(definition.IsLockable).IsTrue();
        await Assert.That(definition.RequiresCoordinatedMutation).IsTrue();
    }

    private static string[] GuardedKeys => PublicationPolicySettingKeys.All.ToArray();
}
