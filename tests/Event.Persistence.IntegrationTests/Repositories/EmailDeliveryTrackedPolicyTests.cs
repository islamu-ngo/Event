
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryTrackedPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReusedSystemContext_RestoresRequestedValueAndReportsCommittedPreviousValue(bool sameTrackedInput)
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using var contextA = CreateContext(databasePath);
            await using var contextB = CreateContext(databasePath);
            var repositoryB = SystemRepository(contextB);
            await ApplyEmailSettingsAsync(context: contextA, mutations: [HostMutation("\"smtp.first.test\"")]);
            var tracked = await contextA.SystemSettings
                .SingleAsync(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
            await ApplyEmailSettingsAsync(context: contextB, mutations: [HostMutation("\"smtp.second.test\"")]);
            var pending = AddUnrelatedPendingSetting(contextA);

            var incoming = HostMutation(sameTrackedInput ? tracked.Value : "\"smtp.first.test\"");
            var result = await CreateEmailSettingsWriter(contextA).ApplyAsync([incoming], actorUserId: null);

            var committed = await repositoryB.GetByKey(GovernanceSettingKeys.Email.SmtpHost);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(committed!.Value).IsEqualTo("\"smtp.first.test\"");
            await Assert.That(result.Changes.Single().PreviousValue).IsEqualTo("\"smtp.second.test\"");
            await AssertPendingSettingSurvivedAsync(repositoryB, pending);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task ReusedSystemLockContext_UnlocksCurrentRowWithoutRestoringStaleValue()
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using var contextA = CreateContext(databasePath);
            await using var contextB = CreateContext(databasePath);
            var repositoryB = SystemRepository(contextB);
            await ApplyEmailSettingsAsync(context: contextA, mutations: [HostMutation("\"smtp.first.test\"")]);
            await ApplyEmailSettingsAsync(context: contextA, mutations:
                [new(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpHost,
                    Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: false)]);
            _ = await contextA.SystemSettings.SingleAsync(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
            await ApplyEmailSettingsAsync(context: contextB, mutations: [HostMutation("\"smtp.second.test\"")]);
            await ApplyEmailSettingsAsync(context: contextB, mutations:
                [new(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpHost,
                    Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: true)]);
            var pending = AddUnrelatedPendingSetting(contextA);

            var result = await CreateEmailSettingsWriter(contextA).ApplyAsync(
                [new(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpHost,
                    Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: false)], actorUserId: null);

            var committed = await repositoryB.GetByKey(GovernanceSettingKeys.Email.SmtpHost);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(committed!.IsLocked).IsFalse();
            await Assert.That(committed.Value).IsEqualTo("\"smtp.second.test\"");
            await Assert.That(result.Changes.Single().PreviousValue).IsEqualTo("\"smtp.second.test\"");
            await AssertPendingSettingSurvivedAsync(repositoryB, pending);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReusedTenantBatchContext_RestoresRequestedValueAndLockFlag(bool lockOnly)
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using var contextA = CreateContext(databasePath);
            await using var contextB = CreateContext(databasePath);
            var actorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(contextA);
            await SetEmailSettingAsync(context: contextA, key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
                value: "false", actorUserId: actorId);
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Tracked SMTP policy",
                Slug = $"tracked-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = DateTime.UtcNow
            };
            contextA.Tenants.Add(tenant);
            await contextA.SaveChangesAsync();
            var repositoryB = TenantRepository(contextB);
            EmailDeliverySettingMutation requested = new(
                TenantId: tenant.Id, Key: GovernanceSettingKeys.Email.SmtpHost,
                Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.first.test\"", IsLocked: false);
            await ApplyEmailSettingsAsync(context: contextA, mutations: [requested], actorUserId: actorId);
            _ = await contextA.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleAsync(setting => setting.TenantId == tenant.Id && setting.SettingKey == requested.Key);
            await ApplyEmailSettingsAsync(context: contextB,
                mutations: [requested with { Value = lockOnly ? requested.Value : "\"smtp.second.test\"", IsLocked = lockOnly }],
                actorUserId: actorId);
            var pending = AddUnrelatedPendingSetting(contextA);

            await ApplyEmailSettingsAsync(context: contextA, mutations: [requested], actorUserId: actorId);

            var committed = await repositoryB.GetByTenantAndKey(tenant.Id, GovernanceSettingKeys.Email.SmtpHost);
            await Assert.That(committed!.Value).IsEqualTo(requested.Value);
            await Assert.That(committed.IsLocked).IsFalse();
            await AssertPendingSettingSurvivedAsync(SystemRepository(contextB), pending);
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static SystemSettingRepository SystemRepository(ExploreDbContext context) =>
        new(context, new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

    private static TenantSettingRepository TenantRepository(ExploreDbContext context) =>
        new(context, new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

    private static EmailDeliverySettingMutation HostMutation(string value) =>
        new(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpHost,
            Kind: EmailDeliverySettingMutationKind.SetValue, Value: value);

    private static SystemSetting AddUnrelatedPendingSetting(ExploreDbContext context)
    {
        var pending = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.AiAssistant.Enabled,
            Value = "true",
            ValueType = SettingValueType.Boolean,
            CreatedAt = DateTime.UtcNow
        };
        context.SystemSettings.Add(pending);
        return pending;
    }

    private static async Task AssertPendingSettingSurvivedAsync(SystemSettingRepository repository, SystemSetting pending)
    {
        var committed = await repository.GetByKey(pending.SettingKey);
        await Assert.That(committed).IsNotNull();
        await Assert.That(committed!.Id).IsEqualTo(pending.Id);
        await Assert.That(committed.Value).IsEqualTo(pending.Value);
    }

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"tracked-email-policy-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }
}
