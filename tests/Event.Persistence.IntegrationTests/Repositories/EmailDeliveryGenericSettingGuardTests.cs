
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
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
            await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => CreateContext(path));
            Guid actorId = authenticated.ActorId;
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
            var writer = CreateInternalEmailSettingsWriter(context, mutationLock);
            var seeded = await writer.ApplyInternalAsync(
                [new(TenantId: null, Key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "false"),
                    new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: operation == GenericWrite.TenantUnlock)],
                actorUserId: actorId);
            await Assert.That(seeded.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            var processorBefore = await context.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => new { row.DeliveryPolicyRevision, row.OptionalSuppressedThroughRevision, row.OptionalSuppressedThroughUtc }).SingleAsync();
            bool rejected = false;
            bool instance = operation is GenericWrite.SystemValue or GenericWrite.SystemTransaction or GenericWrite.SystemLock;
            string admittedKey = operation == GenericWrite.TenantCreateBulk
                ? GovernanceSettingKeys.Email.FromName : GovernanceSettingKeys.Email.DeliveryEnabled;
            string admittedValue = operation == GenericWrite.TenantCreateBulk ? "\"blocked\"" : "false";
            await authenticated.RunAsync(instance ? null : tenantId, async session =>
            {
                var systemRepository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
                var tenantRepository = new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary);
                async Task<bool> Attempt(CancellationToken transactionToken)
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
                    if (operation is GenericWrite.TenantBulk or GenericWrite.TenantCreateBulk)
                    {
                        // The genuine scalar producer admits this protected value but cannot
                        // grant the paired lock effect required by a replacement batch.
                        // Check the email-specific guard separately inside that same owner,
                        // so an unauthenticated or wrong-target denial cannot satisfy the case.
                        await session.Boundary.RequireAsync(session.Context, Explore.Domain.Settings.SettingScope.Tenant,
                            tenantId, null, admittedKey, HumanSettingsMutationKind.SetValue, admittedValue,
                            auditActorId: actorId, cancellationToken: transactionToken);
                        var guarded = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                            tenantRepository.SetValueAsync(tenantId, admittedKey, admittedValue, transactionToken, actorId));
                        await Assert.That(guarded!.Message).Contains("dedicated email");
                    }
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
                                    [new(SettingKey: admittedKey, Value: admittedValue, IsLocked: false),
                                        new(SettingKey: "branding.site_name", Value: "\"must-not-commit\"", IsLocked: false)],
                                    actorId, transactionToken);
                                break;
                            case GenericWrite.TenantCreateBulk:
                                await tenantRepository.CreateManyForTenantAsync(tenantId,
                                    [new(SettingKey: admittedKey, Value: admittedValue, IsLocked: false),
                                        new(SettingKey: "branding.site_name", Value: "\"must-not-commit\"", IsLocked: false)],
                                    actorId, DateTime.UtcNow, transactionToken);
                                break;
                        }
                    }
                    catch (InvalidOperationException error) when (operation is not (GenericWrite.TenantBulk or GenericWrite.TenantCreateBulk))
                    {
                        await Assert.That(error.Message).Contains("dedicated email");
                        rejected = true;
                    }
                    catch (UnauthorizedAccessException) when (operation is GenericWrite.TenantBulk or GenericWrite.TenantCreateBulk)
                    {
                        rejected = true;
                    }
                    await session.Context.SaveChangesAsync(transactionToken);
                    return true;
                }
                return operation switch
                {
                    GenericWrite.SystemLock or GenericWrite.TenantLock => await session.LockAsync(admittedKey, true, Attempt),
                    GenericWrite.TenantUnlock => await session.LockAsync(admittedKey, false, Attempt),
                    GenericWrite.TenantRemove => await session.RemoveAsync(admittedKey, Attempt),
                    _ => await session.ValueAsync(admittedKey,
                        operation == GenericWrite.TenantCreateBulk ? "blocked" : "false", admittedValue, Attempt)
                };
            });
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
            await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => CreateContext(path));
            bool rejected = false;
            await authenticated.RunAsync(null, session => session.ValueAsync(
                    GovernanceSettingKeys.Email.DeliveryEnabled, "false", "false", async token =>
                    {
                        var repository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
                        var canonical = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertAsync(new SystemSetting
                        {
                            SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                            Value = "false"
                        }, token));
                        await Assert.That(canonical!.Message).Contains("dedicated email");
                        // Alias text is not the original key. Reject at exact-effect custody,
                        // before provider collation or the generic writer can resolve it.
                        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.UpsertAsync(new SystemSetting
                        {
                            Id = Guid.CreateVersion7(),
                            SettingKey = key,
                            Value = "false",
                            ValueType = SettingValueType.Boolean,
                            CreatedAt = DateTime.UtcNow
                        }, token));
                        rejected = true;
                        return true;
                    }));
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
