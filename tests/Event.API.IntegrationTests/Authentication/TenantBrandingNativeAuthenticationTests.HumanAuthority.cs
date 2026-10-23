using Event.Persistence.IntegrationTests.Fixtures;
using System.Text.Json;
using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Features.TenantSettingsDocuments.Handlers.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class TenantBrandingNativeAuthenticationTests
{
    [Test]
    [Arguments("foreignTenant")]
    [Arguments("rolledBack")]
    [Arguments("unfenced")]
    public async Task NativeDocumentTenantForeignKeyCannotBorrowUnfencedOrForeignAuthority(string scenario)
    {
        Guid tenant = await SeedTenantAsync();
        Guid foreignTenant = await SeedTenantAsync();
        await using var identity = await TenantBrandingNativeAuthenticationFixture.CreateAsync(fixture, tenant);
        var owner = identity.Owner;
        await owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            await identity.InOriginalRequestAsync(async () =>
            {
                var sourceSets = identity.Services.GetRequiredService<IPolicySourceSetFence>();
                var principal = identity.Services.GetRequiredService<IHumanPrincipalSourceFence>();
                if (scenario == "unfenced")
                    await principal.ObserveAsync();
                await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                if (scenario != "unfenced")
                    await principal.AcquireAsync(await sourceSets.AcquireAsync());
                if (scenario == "rolledBack")
                    await transaction.RollbackAsync();
                await using var replacement = scenario == "rolledBack"
                    ? await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
                var candidate = TenantBrandingSettingsDocumentDefaults.Create(
                    scenario == "foreignTenant" ? foreignTenant : tenant, "Rejected");
                owner.TenantSettingsDocuments.Add(candidate);
                if (scenario == "unfenced")
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => owner.SaveChangesAsync());
                    await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                }
                else
                {
                    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => owner.SaveChangesAsync());
                    await Assert.That(error!.Message).IsEqualTo("tracked_tenant_authority_mismatch");
                }
                await owner.Database.CurrentTransaction!.RollbackAsync();
                owner.Entry(candidate).State = EntityState.Detached;
                return true;
            }));
        await using var observation = fixture.CreateDbContext();
        await Assert.That(await observation.TenantSettingsDocuments.AnyAsync(
            row => row.TenantId == tenant || row.TenantId == foreignTenant)).IsFalse();
    }

    private async Task<string?> SetInstanceValueAsync(
        AuthenticatedSettingsFixture identity, string key, string value)
    {
        using var stored = JsonDocument.Parse(value);
        string input = stored.RootElement.ValueKind == JsonValueKind.String
            ? stored.RootElement.GetString()! : value;
        await using var observation = fixture.CreateDbContext();
        var setting = await observation.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key)
            ?? throw new InvalidOperationException($"The declared setting {key} was not seeded.");
        setting.Value = value;
        return await identity.RunAsync(null, session => session.ValueAsync(key, input, value, token =>
        {
            setting.UpdatedBy = session.ActorId;
            var repository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
            return repository.UpsertAsync(setting, token);
        }));
    }

    private async Task<string?> SetInstanceLockAsync(
        AuthenticatedSettingsFixture identity, string key, bool locked)
    {
        await using var observation = fixture.CreateDbContext();
        var setting = await observation.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key)
            ?? throw new InvalidOperationException($"The declared setting {key} was not seeded.");
        setting.IsLocked = locked;
        return await identity.RunAsync(null, session => session.LockAsync(key, locked, token =>
        {
            setting.UpdatedBy = session.ActorId;
            var repository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
            return repository.UpsertLockAsync(setting, token);
        }));
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("unchanged")]
    [Arguments("unrelated")]
    public async Task Patch_AfterPreflight_FencesTenantGrantBeforeDocumentObservation(string change)
    {
        Guid tenantId = await SeedTenantAsync();
        await using var identity = await TenantBrandingNativeAuthenticationFixture.CreateAsync(fixture, tenantId);
        await using var setup = fixture.CreateDbContext();
        await using var settingsIdentity = await AuthenticatedSettingsFixture.CreateAsync(
            () => fixture.CreateDbContext(), fixture);
        await SetInstanceValueAsync(settingsIdentity, GovernanceSettingKeys.Deployment.Mode, "\"SingleTenant\"");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var original = await Provisioner(setup, Resolver(setup, cache))
            .EnsureTenantBrandingDocumentAsync(tenantId, "Original");
        await setup.Entry(original).ReloadAsync();
        Guid stamp = original.ConcurrencyStamp;
        string json = original.PayloadJson;
        setup.Entry(original).State = EntityState.Detached;
        var owner = identity.Owner;
        var resolver = Resolver(owner, cache);
        bool preflightPassed = false;
        var unit = new EfCoreUnitOfWork(owner);
        var mutationLock = new RelationalSettingMutationLock(owner, unit, async (_, token) =>
        {
            if (preflightPassed) return;
            preflightPassed = true;
            await Assert.That(owner.Database.CurrentTransaction).IsNull();
            if (change == "unchanged") return;
            await using var writer = fixture.CreateDbContext();
            int affected = change == "revoked"
                ? await writer.TenantUserRoleGrants.Where(row => row.Id == identity.TenantGrantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow), token)
                : await writer.PlatformUserRoles.Where(row => row.Id == identity.PlatformGrantId).ExecuteDeleteAsync(token);
            await Assert.That(affected).IsEqualTo(1);
        });
        var lockService = new TenantBrandingSettingsDocumentLockService(
            SystemSettingRepository.CreateReadOnly(owner, mutationLock), mutationLock);
        var handler = new PatchTenantBrandingSettingsDocumentCommandHandler(
            new TenantSettingsDocumentRepository(owner), unit, resolver, lockService,
            identity.HumanScope, identity.TenantContext, identity.Authority, identity.Preparation, identity.Finalizer);
        var response = await identity.InOriginalRequestAsync(() => handler.ExecuteAsync(new PatchTenantBrandingSettingsDocumentCommand
        {
            TenantId = tenantId,
            Patch = new PatchTenantBrandingSettingsDocumentDto
            {
                ExpectedConcurrencyStamp = stamp,
                DisplayName = new() { Value = OptionalUpdate<string?>.Set("Patched") }
            }
        }, CancellationToken.None));
        await Assert.That(preflightPassed).IsTrue();
        await Assert.That(response.IsSuccess).IsEqualTo(change != "revoked");
        await using var observation = fixture.CreateDbContext();
        var persisted = (await new TenantSettingsDocumentRepository(observation)
            .GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!;
        var persistedGrant = await observation.TenantUserRoleGrants.AsNoTracking()
            .SingleAsync(row => row.Id == identity.TenantGrantId);
        await Assert.That(persistedGrant.RevokedAt.HasValue).IsEqualTo(change == "revoked");
        await Assert.That(await observation.PlatformUserRoles.AsNoTracking()
            .AnyAsync(row => row.Id == identity.PlatformGrantId)).IsEqualTo(change != "unrelated");
        if (change == "revoked")
        {
            // An unchanged instance grant cannot replace exact tenant-administrator authority.
            await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(stamp);
            await Assert.That(persisted.PayloadJson).IsEqualTo(json);
        }
        else
        {
            await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(stamp);
            await Assert.That(response.Id!.Payload.DisplayName).IsEqualTo("Patched");
        }
    }
}
