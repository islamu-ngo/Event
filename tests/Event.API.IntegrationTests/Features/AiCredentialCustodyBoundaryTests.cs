using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class AiCredentialCustodyBoundaryTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task TrackedCurrentOrOriginalRetiredKeyCannotPersist(bool tenant, bool original)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        string canary = Guid.CreateVersion7().ToString("N");
        Guid id = Guid.CreateVersion7();
        Guid tenantId = db.TenantContext!.TenantId;
        if (original)
        {
            if (tenant)
            {
                db.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = id,
                    TenantId = tenantId,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.AiAssistant.CredentialScope,
                    Value = "\"none\""
                });
                await db.SaveChangesAsync();
                await db.TenantSettingOverrides.Where(row => row.Id == id).ExecuteUpdateAsync(setters =>
                    setters.SetProperty(row => row.SettingKey, AiCredentialReference.RetiredSettingKey)
                        .SetProperty(row => row.Value, canary));
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Id = id,
                    SettingKey = "ai-custody-fixture",
                    Value = "\"none\"",
                    ValueType = SettingValueType.String
                });
                await db.SaveChangesAsync();
                await db.SystemSettings.Where(row => row.Id == id).ExecuteUpdateAsync(setters =>
                    setters.SetProperty(row => row.SettingKey, AiCredentialReference.RetiredSettingKey)
                        .SetProperty(row => row.Value, canary));
            }
            db.ChangeTracker.Clear();
            if (tenant)
                (await db.TenantSettingOverrides.SingleAsync(row => row.Id == id)).SettingKey = "ai-custody-renamed";
            else
                (await db.SystemSettings.SingleAsync(row => row.Id == id)).SettingKey = "ai-custody-renamed";
        }
        else if (tenant)
            db.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = id,
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = AiCredentialReference.RetiredSettingKey,
                Value = canary
            });
        else
            db.SystemSettings.Add(new SystemSetting
            {
                Id = id,
                SettingKey = AiCredentialReference.RetiredSettingKey,
                Value = canary,
                ValueType = SettingValueType.String
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        if (original)
        {
            string? stored = tenant
                ? await db.TenantSettingOverrides.AsNoTracking().Where(row => row.Id == id).Select(row => row.Value).SingleAsync()
                : await db.SystemSettings.AsNoTracking().Where(row => row.Id == id).Select(row => row.Value).SingleAsync();
            await Assert.That(stored).IsEqualTo(canary);
        }
        else
        {
            bool present = tenant
                ? await db.TenantSettingOverrides.AsNoTracking().AnyAsync(row => row.Id == id)
                : await db.SystemSettings.AsNoTracking().AnyAsync(row => row.Id == id);
            await Assert.That(present).IsFalse();
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task OrdinaryRepositoryValueAndLockWritesRejectRetiredAiKey(int operation)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid tenant = db.TenantContext!.TenantId;
        var system = scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantSettingRepository>();
        var setting = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = AiCredentialReference.RetiredSettingKey,
            Value = Guid.CreateVersion7().ToString("N"),
            ValueType = SettingValueType.String
        };
        Task Write() => operation switch
        {
            0 => system.UpsertAsync(setting),
            1 => system.UpsertLockAsync(setting),
            2 => system.UpsertInCurrentTransactionAsync(setting),
            3 => tenants.SetValueAsync(tenant, setting.SettingKey, setting.Value),
            4 => tenants.LockAsync(tenant, setting.SettingKey, Guid.CreateVersion7()),
            5 => tenants.UnlockAsync(tenant, setting.SettingKey, Guid.CreateVersion7()),
            6 => tenants.UpsertManyForTenantAsync(tenant,
                [new TenantSettingOverrideUpsert(setting.SettingKey, setting.Value, false)], Guid.CreateVersion7()),
            7 => tenants.CreateManyForTenantAsync(tenant,
                [new TenantSettingOverrideUpsert(setting.SettingKey, setting.Value, false)], null, DateTime.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Write);

        await Assert.That(exception!.Message).IsEqualTo("ai_credential_setting_retired");
        await Assert.That(await db.SystemSettings.AnyAsync(row => row.SettingKey == setting.SettingKey)).IsFalse();
        await Assert.That(await db.TenantSettingOverrides.AnyAsync(row => row.SettingKey == setting.SettingKey)).IsFalse();
    }
}
