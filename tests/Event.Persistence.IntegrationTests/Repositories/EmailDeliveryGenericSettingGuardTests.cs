
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Mail;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryGenericSettingGuardTests
{
    public enum GenericWrite { SystemValue, SystemTransaction, SystemLock, TenantValue, TenantRemove, TenantLock, TenantUnlock, TenantBulk, TenantCreateBulk }

    [Test]
    [Arguments(GenericWrite.SystemValue)]
    [Arguments(GenericWrite.SystemTransaction)]
    [Arguments(GenericWrite.SystemLock)]
    [Arguments(GenericWrite.TenantValue)]
    [Arguments(GenericWrite.TenantRemove)]
    [Arguments(GenericWrite.TenantLock)]
    [Arguments(GenericWrite.TenantUnlock)]
    [Arguments(GenericWrite.TenantBulk)]
    [Arguments(GenericWrite.TenantCreateBulk)]
    public async Task GenericEntryPoint_RejectsBeforeAnyMutationEvenWhenCallerCommits(GenericWrite operation)
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            Guid actorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
            Guid tenantId = Guid.CreateVersion7();
            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = "SMTP guard tenant",
                Slug = $"smtp-guard-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var writer = new EmailDeliverySettingsWriter(context, mutationLock, unitOfWork,
                new EmailDeliveryDisableImpactReader(context), new EmailDeliveryDisableTokenService(new EphemeralDataProtectionProvider()));
            var seeded = await writer.ApplyAsync(
                [new(TenantId: null, Key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "false"),
                    new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: operation == GenericWrite.TenantUnlock)],
                actorUserId: actorId);
            await Assert.That(seeded.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            var processorBefore = await context.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => new { row.DeliveryPolicyRevision, row.OptionalSuppressedThroughRevision, row.OptionalSuppressedThroughUtc }).SingleAsync();
            var systemRepository = new SystemSettingRepository(context, mutationLock);
            var tenantRepository = new TenantSettingRepository(context, mutationLock);
            bool rejected = false;
            await mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => unitOfWork.ExecuteSerializableAsync(async transactionToken =>
                {
                    var system = new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                        Value = "false",
                        ValueType = SettingValueType.Boolean,
                        IsLocked = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    try
                    {
                        switch (operation)
                        {
                            case GenericWrite.SystemValue: await systemRepository.UpsertAsync(system, transactionToken); break;
                            case GenericWrite.SystemTransaction: await systemRepository.UpsertInCurrentTransactionAsync(system, transactionToken); break;
                            case GenericWrite.SystemLock: await systemRepository.UpsertLockAsync(system, transactionToken); break;
                            case GenericWrite.TenantValue: await tenantRepository.SetValueAsync(tenantId, system.SettingKey, "false", transactionToken, actorId); break;
                            case GenericWrite.TenantRemove: await tenantRepository.RemoveOverrideAsync(tenantId, system.SettingKey, transactionToken); break;
                            case GenericWrite.TenantLock: await tenantRepository.LockAsync(tenantId, system.SettingKey, actorId, transactionToken); break;
                            case GenericWrite.TenantUnlock: await tenantRepository.UnlockAsync(tenantId, system.SettingKey, actorId, transactionToken); break;
                            case GenericWrite.TenantBulk:
                                await tenantRepository.UpsertManyForTenantAsync(tenantId,
                                    [new(SettingKey: "branding.site_name", Value: "\"must-not-commit\"", IsLocked: false),
                                        new(SettingKey: system.SettingKey, Value: "false", IsLocked: false)], actorId, transactionToken);
                                break;
                            case GenericWrite.TenantCreateBulk:
                                await tenantRepository.CreateManyForTenantAsync(tenantId,
                                    [new(SettingKey: "branding.site_name", Value: "\"must-not-commit\"", IsLocked: false),
                                        new(SettingKey: GovernanceSettingKeys.Email.FromName, Value: "\"blocked\"", IsLocked: false)],
                                    actorId, DateTime.UtcNow, transactionToken);
                                break;
                        }
                    }
                    catch (InvalidOperationException) { rejected = true; }
                    await context.SaveChangesAsync(transactionToken);
                    return true;
                }, token));
            await Assert.That(rejected).IsTrue();
            await using var observer = CreateContext(path);
            var instanceSetting = await observer.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
            await Assert.That(instanceSetting.Value).IsEqualTo("true");
            await Assert.That(instanceSetting.IsLocked).IsFalse();
            await Assert.That(await observer.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => new { row.DeliveryPolicyRevision, row.OptionalSuppressedThroughRevision, row.OptionalSuppressedThroughUtc }).SingleAsync())
                .IsEqualTo(processorBefore);
            var overrides = await observer.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking().Where(row => row.TenantId == tenantId).ToListAsync();
            await Assert.That(overrides.Count).IsEqualTo(1);
            await Assert.That(overrides[0].Value).IsEqualTo("true");
            await Assert.That(overrides[0].IsLocked).IsEqualTo(operation == GenericWrite.TenantUnlock);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments("EMAIL.DELIVERY_ENABLED")]
    [Arguments(" email.delivery_enabled ")]
    [Arguments("email.delívery_enabled")]
    public async Task GenericAlias_CannotReachCollationDependentProtectedRows(string key)
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            bool rejected = false;
            try
            {
                await new SystemSettingRepository(context, new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)))
                    .UpsertAsync(new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = key,
                        Value = "false",
                        ValueType = SettingValueType.Boolean,
                        CreatedAt = DateTime.UtcNow
                    });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { rejected = true; }
            await Assert.That(rejected).IsTrue();
            await Assert.That((await context.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value).IsEqualTo("true");
        }
        finally { DeleteDatabase(path); }
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"email-generic-guard-{Guid.CreateVersion7():N}.db");
    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }
}
