using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed partial class OrdinarySettingsNativeContractTests
{
    private const string ReadIsolationProof = "Independently verify original ordinary read scope and rollback using Migrator.";

    [Test]
    public async Task CurrentOrdinaryEntityCopiesCannotMutateTheOriginalImageOrReplaceItsNativeWrite()
    {
        await using var authentication = await CreateManagedDisclosureOwnerAsync(new ManagedDisclosureCommitProbe(false));
        var targets = await SeedOrdinaryReadTargetsAsync();
        OrdinaryReadState before = null!;
        await authentication.RunAsync(targets.Own, async session =>
        {
            before = await ReadOrdinaryReadStateAsync(targets);
            return await ExecuteOrdinaryReadAsync(session, SettingScope.Tenant, async token =>
            {
                var system = session.Services.GetRequiredService<ISystemSettingRepository>();
                var tenant = session.Services.GetRequiredService<ITenantSettingRepository>();
                var firstSystem = await system.GetByKey(TenantDisclosureKey, token);
                var firstTenant = await tenant.GetByTenantAndKey(targets.Own, TenantDisclosureKey, token);
                await Assert.That(firstSystem).IsNotNull();
                await Assert.That(firstTenant).IsNotNull();
                await Assert.That(session.Context.Entry(firstSystem!).State).IsEqualTo(EntityState.Detached);
                await Assert.That(session.Context.Entry(firstTenant!).State).IsEqualTo(EntityState.Detached);
                firstSystem!.Value = "forged-copy";
                firstSystem.IsLocked = true;
                firstTenant!.Value = "forged-copy";
                firstTenant.IsLocked = true;

                var currentSystem = await system.GetByKey(TenantDisclosureKey, token);
                var currentTenant = await tenant.GetByTenantAndKey(targets.Own, TenantDisclosureKey, token);
                await Assert.That(ReferenceEquals(firstSystem, currentSystem)).IsFalse();
                await Assert.That(ReferenceEquals(firstTenant, currentTenant)).IsFalse();
                await Assert.That(currentSystem!.Value).IsEqualTo(before.SystemValue);
                await Assert.That(currentSystem.IsLocked).IsFalse();
                await Assert.That(currentTenant!.Id).IsEqualTo(before.OwnId);
                await Assert.That(currentTenant.Value).IsEqualTo(before.OwnValue);
                await Assert.That(currentTenant.IsLocked).IsFalse();
                await Assert.That(await system.IsLocked(TenantDisclosureKey, token)).IsFalse();

                await tenant.SetValueAsync(targets.Own, TenantDisclosureKey, "true", token, session.ActorId);
                var completed = await tenant.GetByTenantAndKey(targets.Own, TenantDisclosureKey, token);
                await Assert.That(completed!.Id).IsEqualTo(before.OwnId);
                await Assert.That(completed.Value).IsEqualTo("true");
                await Assert.That(completed.UpdatedBy).IsEqualTo(session.ActorId);
                return true;
            });
        });
        var after = await ReadOrdinaryReadStateAsync(targets);
        await Assert.That(after).IsEqualTo(before with
        {
            OwnValue = "true",
            OwnDisclosure = checked(before.OwnDisclosure + 1)
        });
    }

    [Test]
    public async Task UnobservedKeyReadCannotBeCaughtToCompleteTheAdmittedOrdinaryAttempt()
    {
        await using var authentication = await CreateManagedDisclosureOwnerAsync(new ManagedDisclosureCommitProbe(false));
        var targets = await SeedOrdinaryReadTargetsAsync();
        OrdinaryReadState before = null!;
        bool admitted = false, rejected = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.RunAsync(null, async session =>
        {
            before = await ReadOrdinaryReadStateAsync(targets);
            return await ExecuteOrdinaryReadAsync(session, SettingScope.Instance, async token =>
            {
                var repository = session.Services.GetRequiredService<ISystemSettingRepository>();
                await Assert.That(await repository.GetByKey(TenantDisclosureKey, token)).IsNotNull();
                admitted = true;
                var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetByKey(Key, token));
                await Assert.That(refusal.Message).IsEqualTo("An ordinary read requires its original captured key.");
                rejected = true;
                var poisoned = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    repository.GetByKey(TenantDisclosureKey, token));
                await Assert.That(poisoned.Message).IsEqualTo("The original ordinary source is not active.");
                return true;
            });
        }));
        await Assert.That(admitted).IsTrue();
        await Assert.That(rejected).IsTrue();
        await Assert.That(await ReadOrdinaryReadStateAsync(targets)).IsEqualTo(before);
    }

    [Test]
    public async Task ForeignTenantReadCannotBeCaughtToCompleteTheAdmittedOrdinaryAttempt()
    {
        await using var authentication = await CreateManagedDisclosureOwnerAsync(new ManagedDisclosureCommitProbe(false));
        var targets = await SeedOrdinaryReadTargetsAsync();
        OrdinaryReadState before = null!;
        bool admitted = false, rejected = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.RunAsync(targets.Own, async session =>
        {
            before = await ReadOrdinaryReadStateAsync(targets);
            return await ExecuteOrdinaryReadAsync(session, SettingScope.Tenant, async token =>
            {
                var repository = session.Services.GetRequiredService<ITenantSettingRepository>();
                await Assert.That(await repository.GetByTenantAndKey(targets.Own, TenantDisclosureKey, token)).IsNotNull();
                admitted = true;
                var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    repository.GetByTenantAndKey(targets.Foreign, TenantDisclosureKey, token));
                await Assert.That(refusal.Message).IsEqualTo("An ordinary read cannot select another tenant.");
                rejected = true;
                var poisoned = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    repository.GetByTenantAndKey(targets.Own, TenantDisclosureKey, token));
                await Assert.That(poisoned.Message).IsEqualTo("The original ordinary source is not active.");
                return true;
            });
        }));
        await Assert.That(admitted).IsTrue();
        await Assert.That(rejected).IsTrue();
        await Assert.That(await ReadOrdinaryReadStateAsync(targets)).IsEqualTo(before);
    }

    [Test]
    public async Task ExpiringTheAdmittedOriginalSourceRevokesItsRetainedEntityReads()
    {
        await using var authentication = await CreateManagedDisclosureOwnerAsync(new ManagedDisclosureCommitProbe(false));
        var targets = await SeedOrdinaryReadTargetsAsync();
        OrdinaryReadState before = null!;
        bool admitted = false, rejected = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authentication.RunAsync(null, async session =>
        {
            before = await ReadOrdinaryReadStateAsync(targets);
            return await ExecuteOrdinaryReadAsync(session, SettingScope.Instance, async token =>
            {
                var repository = session.Services.GetRequiredService<ISystemSettingRepository>();
                await Assert.That(await repository.GetByKey(TenantDisclosureKey, token)).IsNotNull();
                admitted = true;
                var source = session.Preparation.CurrentOrdinarySource!;
                session.Services.GetRequiredService<IOrdinarySettingsMutationStore>().Expire(source);
                var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    repository.GetByKey(TenantDisclosureKey, token));
                await Assert.That(refusal.Message).IsEqualTo("The original ordinary source is not active.");
                rejected = true;
                return true;
            });
        }));
        await Assert.That(admitted).IsTrue();
        await Assert.That(rejected).IsTrue();
        await Assert.That(await ReadOrdinaryReadStateAsync(targets)).IsEqualTo(before);
    }

    private static async Task<T> ExecuteOrdinaryReadAsync<T>(AuthenticatedSettingsFixture.Session session,
        SettingScope scope, Func<CancellationToken, Task<T>> operation)
    {
        var prepared = await session.Preparation.PrepareAsync(
            new UpdateSettingCommand { Key = TenantDisclosureKey, Scope = scope, Value = "true" }, "true");
        return await session.MutationLock.ExecuteOrderedGroupsAsync([[TenantDisclosureKey]],
            token => session.Preparation.ExecutePreparedAsync(prepared, async (_, ownerToken) =>
            {
                await Assert.That(session.Preparation.CurrentOrdinarySource).IsNotNull();
                await Assert.That(session.Context.HumanCommitAttemptOwnsTransaction).IsTrue();
                await Assert.That(session.Context.Database.CurrentTransaction).IsNotNull();
                return await operation(ownerToken);
            }, token));
    }

    private async Task<OrdinaryReadTargets> SeedOrdinaryReadTargetsAsync()
    {
        await using var seed = Open(Explore.Secrets.Database.PrimaryDatabaseRole.Migrator);
        var tenants = await seed.Tenants.AsNoTracking()
            .Where(row => row.FullName == "Ordinary native fixture").ToListAsync();
        Guid own = tenants.Single(row => row.TenantStatusId == (int)TenantStatusEnum.Active).Id;
        Guid foreign = tenants.Single(row => row.TenantStatusId == (int)TenantStatusEnum.Suspended).Id;
        seed.AddRange(new[] { own, foreign }.Select(id => new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = id,
            Tenant = null!,
            SettingKey = TenantDisclosureKey,
            Value = "false",
            IsLocked = false,
            CreatedAt = Instant
        }));
        await seed.SaveChangesAsync();
        return new(own, foreign);
    }

    private async Task<OrdinaryReadState> ReadOrdinaryReadStateAsync(OrdinaryReadTargets targets)
    {
        await using var observer = Open(Explore.Secrets.Database.PrimaryDatabaseRole.Migrator);
        var system = await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == TenantDisclosureKey);
        var tenants = await observer.Set<TenantSetting>().IgnoreTenantFilter(ReadIsolationProof).AsNoTracking()
            .Where(row => row.SettingKey == TenantDisclosureKey && (row.TenantId == targets.Own || row.TenantId == targets.Foreign))
            .ToListAsync();
        var revisions = await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(ReadIsolationProof).AsNoTracking()
            .Where(row => row.TenantId == targets.Own || row.TenantId == targets.Foreign).ToListAsync();
        var own = tenants.Single(row => row.TenantId == targets.Own);
        var foreign = tenants.Single(row => row.TenantId == targets.Foreign);
        return new(system.Id, system.Value, own.Id, own.Value, foreign.Id, foreign.Value,
            revisions.Single(row => row.TenantId == targets.Own).DisclosureEpoch,
            revisions.Single(row => row.TenantId == targets.Foreign).DisclosureEpoch);
    }

    private sealed record OrdinaryReadTargets(Guid Own, Guid Foreign);
    private sealed record OrdinaryReadState(Guid SystemId, string SystemValue, Guid OwnId, string OwnValue,
        Guid ForeignId, string ForeignValue, long OwnDisclosure, long ForeignDisclosure);
}
