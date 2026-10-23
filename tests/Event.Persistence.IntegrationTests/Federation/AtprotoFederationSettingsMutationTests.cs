using System.Collections.Immutable;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Federation;

/// <summary>Exercises real Local human preparation and the original native owner, not substitute authority.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class AtprotoFederationSettingsMutationTests(PostgreSqlContainerFixture fixture)
{
    private const string Key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
    private const string SecondKey = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled;
    private const string IsolationProof = "Verify committed federation effects and untouched foreign rows using Migrator.";
    private static readonly DateTime CreatedAt = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(SettingScope.Instance)]
    [Arguments(SettingScope.Tenant)]
    public async Task PreparedValueWritesDedicatedCanonicalAuditAndEpochOnTheOriginalOwner(SettingScope scope)
    {
        await using var scenario = await CreateScenarioAsync();
        DatabaseState before = null!;
        await scenario.Authentication.RunAsync(scope == SettingScope.Instance ? null : scenario.Tenant, async session =>
        {
            // Authentication first persists any required tenant membership. Its
            // disclosure effect is fixture setup, not part of this original command.
            before = await ReadAsync(scenario);
            var original = new UpdateSettingCommand { Key = Key, Value = "true", Scope = scope };
            var prepared = await session.Preparation.PrepareAsync(original, "true");
            return await ExecuteAsync(session, prepared, [Key], async token =>
            {
                var transaction = session.Context.Database.CurrentTransaction;
                var connection = session.Context.Database.GetDbConnection();
                await Assert.That(transaction).IsNotNull();
                await Assert.That(session.Context.TenantFilterTenantId)
                    .IsEqualTo(scope == SettingScope.Instance ? null : (Guid?)scenario.Tenant);
                var result = await Writer(session).ApplyCurrentAsync(ValueEffect(scope, scenario.Tenant, Key, "true"), token);
                await Assert.That(result.Applied).IsTrue();
                await Assert.That(result.PreviousValue).IsEqualTo("false");
                await Assert.That(result.CurrentValue).IsEqualTo("true");
                await Assert.That(session.Context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(session.Context.Database.GetDbConnection()).IsSameReferenceAs(connection);
                return true;
            });
        });

        var after = await ReadAsync(scenario);
        var originalRow = scope == SettingScope.Instance ? before.Instance : before.Target!;
        var changed = scope == SettingScope.Instance ? after.Instance : after.Target!;
        await Assert.That(changed.Id).IsEqualTo(originalRow.Id);
        await Assert.That(changed.Value).IsEqualTo("true");
        await Assert.That(changed.IsLocked).IsEqualTo(originalRow.IsLocked);
        await Assert.That(changed.CreatedAt).IsEqualTo(originalRow.CreatedAt);
        await Assert.That(changed.CreatedBy).IsEqualTo(originalRow.CreatedBy);
        await Assert.That(changed.UpdatedBy).IsEqualTo(scenario.Authentication.ActorId);
        await Assert.That(changed.UpdatedAt).IsNotNull();
        await Assert.That(changed.UpdatedAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        if (scope == SettingScope.Instance)
            await Assert.That(after.Target).IsEqualTo(before.Target);
        else
            await Assert.That(after.Instance).IsEqualTo(before.Instance);
        await Assert.That(after.InstanceBackfill).IsEqualTo(before.InstanceBackfill);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        await AssertEpochsAsync(before, after, root: scope == SettingScope.Instance);
        await AssertLegacyUnchangedAsync(before, after);
    }

    [Test]
    public async Task PreparedTenantValueCreatesMissingOverrideThroughItsOriginalParentWitness()
    {
        await using var scenario = await CreateScenarioAsync(missingTarget: true);
        DatabaseState before = null!;
        Guid? emittedId = null;
        await scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            await Assert.That(before.Target).IsNull();
            var prepared = await session.Preparation.PrepareAsync(
                new UpdateSettingCommand { Key = Key, Value = "true", Scope = SettingScope.Tenant }, "true");
            return await ExecuteAsync(session, prepared, [Key], async token =>
            {
                var result = await Writer(session).ApplyCurrentAsync(ValueEffect(SettingScope.Tenant, scenario.Tenant, Key, "true"), token);
                await Assert.That(result.Applied).IsTrue();
                await Assert.That(result.EntityId).IsNotNull();
                emittedId = result.EntityId;
                return true;
            });
        });

        var after = await ReadAsync(scenario);
        await Assert.That(after.Target).IsNotNull();
        await Assert.That(emittedId).IsNotNull();
        await Assert.That(after.Target!.Id).IsEqualTo(emittedId!.Value);
        await Assert.That(after.Target.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(after.Target.Value).IsEqualTo("true");
        await Assert.That(after.Target.IsLocked).IsFalse();
        await Assert.That(after.Target.CreatedBy).IsEqualTo(scenario.Authentication.ActorId);
        await Assert.That(after.Target.CreatedAt.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(after.Instance).IsEqualTo(before.Instance);
        await Assert.That(after.InstanceBackfill).IsEqualTo(before.InstanceBackfill);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        await AssertEpochsAsync(before, after);
        await AssertLegacyUnchangedAsync(before, after);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PreparedTenantLockChangesOnlyItsCanonicalLockAndAudit(bool locked)
    {
        await using var scenario = await CreateScenarioAsync(initiallyLocked: !locked);
        DatabaseState before = null!;
        await scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            var prepared = locked
                ? await session.Preparation.PrepareAsync(new LockSettingCommand { Key = Key, Scope = SettingScope.Tenant })
                : await session.Preparation.PrepareAsync(new UnlockSettingCommand { Key = Key, Scope = SettingScope.Tenant });
            return await ExecuteAsync(session, prepared, [Key], async token =>
            {
                var effect = new HumanSettingsMutation(HumanSettingsOperation.TenantSettings, SettingScope.Tenant,
                    scenario.Tenant, null, Key, HumanSettingsMutationKind.SetLock, IsLocked: locked);
                var result = await Writer(session).ApplyCurrentAsync(effect, token);
                await Assert.That(result.Applied).IsTrue();
                await Assert.That(result.PreviousLock).IsEqualTo(!locked);
                await Assert.That(result.CurrentLock).IsEqualTo(locked);
                return true;
            });
        });

        var after = await ReadAsync(scenario);
        await Assert.That(after.Target!.Id).IsEqualTo(before.Target!.Id);
        await Assert.That(after.Target.Value).IsEqualTo(before.Target.Value);
        await Assert.That(after.Target.IsLocked).IsEqualTo(locked);
        await Assert.That(after.Target.CreatedAt).IsEqualTo(before.Target.CreatedAt);
        await Assert.That(after.Target.UpdatedBy).IsEqualTo(scenario.Authentication.ActorId);
        await Assert.That(after.Target.UpdatedAt).IsNotNull();
        await Assert.That(after.Instance).IsEqualTo(before.Instance);
        await Assert.That(after.InstanceBackfill).IsEqualTo(before.InstanceBackfill);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        await AssertEpochsAsync(before, after);
        await AssertLegacyUnchangedAsync(before, after);
    }

    [Test]
    public async Task PreparedResetDeletesOnlyTheOriginalDedicatedTenantOverride()
    {
        await using var scenario = await CreateScenarioAsync();
        DatabaseState before = null!;
        await scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            var prepared = await session.Preparation.PrepareAsync(new ResetSettingCommand { Key = Key, Scope = SettingScope.Tenant });
            return await ExecuteAsync(session, prepared, [Key], async token =>
            {
                var effect = new HumanSettingsMutation(HumanSettingsOperation.TenantSettings, SettingScope.Tenant,
                    scenario.Tenant, null, Key, HumanSettingsMutationKind.Remove);
                var result = await Writer(session).ApplyCurrentAsync(effect, token);
                await Assert.That(result.Applied).IsTrue();
                await Assert.That(result.EntityId).IsEqualTo(before.Target!.Id);
                await Assert.That(result.CurrentValue).IsNull();
                return true;
            });
        });

        var after = await ReadAsync(scenario);
        await Assert.That(after.Target).IsNull();
        await Assert.That(after.Instance).IsEqualTo(before.Instance);
        await Assert.That(after.InstanceBackfill).IsEqualTo(before.InstanceBackfill);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        await AssertEpochsAsync(before, after);
        await AssertLegacyUnchangedAsync(before, after);
    }

    [Test]
    public async Task CallbackFailureAfterCanonicalWriteRollsBackItsRowAuditAndEpoch()
    {
        await using var scenario = await CreateScenarioAsync();
        DatabaseState before = null!;
        bool appliedBeforeFailure = false;
        await Assert.ThrowsAsync<DeliberateRollbackException>(() => scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            var prepared = await session.Preparation.PrepareAsync(
                new UpdateSettingCommand { Key = Key, Value = "true", Scope = SettingScope.Tenant }, "true");
            return await ExecuteAsync<bool>(session, prepared, [Key], async token =>
            {
                var result = await Writer(session).ApplyCurrentAsync(ValueEffect(SettingScope.Tenant, scenario.Tenant, Key, "true"), token);
                await Assert.That(result.Applied).IsTrue();
                appliedBeforeFailure = true;
                throw new DeliberateRollbackException();
            });
        }));
        await Assert.That(appliedBeforeFailure).IsTrue();
        await Assert.That(await ReadAsync(scenario)).IsEqualTo(before);
    }

    [Test]
    public async Task ChangedDedicatedParentAfterObservationRejectsBeforeCallbackAdmission()
    {
        await using var scenario = await CreateScenarioAsync();
        DatabaseState before = null!;
        bool callbackAdmitted = false;
        await scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            var prepared = await session.Preparation.PrepareAsync(
                new UpdateSettingCommand { Key = Key, Value = "true", Scope = SettingScope.Tenant }, "true");
            await using (var competitor = OpenMigrator())
            {
                int changed = await competitor.Set<AtprotoFederationInstanceSetting>()
                    .Where(row => row.SettingKey == Key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsLocked, true)
                        .SetProperty(row => row.UpdatedAt, DateTime.UtcNow));
                await Assert.That(changed).IsEqualTo(1);
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteAsync(session, prepared, [Key], async token =>
            {
                callbackAdmitted = true;
                return await Writer(session).ApplyCurrentAsync(ValueEffect(SettingScope.Tenant, scenario.Tenant, Key, "true"), token);
            }));
            return true;
        });
        await Assert.That(callbackAdmitted).IsFalse();
        var after = await ReadAsync(scenario);
        await Assert.That(after.Instance.IsLocked).IsTrue();
        await Assert.That(after.Target).IsEqualTo(before.Target);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        await Assert.That(after.TargetRevision).IsEqualTo(before.TargetRevision);
        await Assert.That(after.ForeignRevision).IsEqualTo(before.ForeignRevision);
        await AssertLegacyUnchangedAsync(before, after);
    }

    [Test]
    public async Task CaughtForeignEffectFailureCannotCommitAnEarlierCanonicalEffect()
    {
        await using var scenario = await CreateScenarioAsync();
        DatabaseState before = null!;
        bool firstApplied = false, invalidEffectRejected = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            var original = new UpdateSettingBatchCommand
            {
                Category = SettingRegistry.Get(Key)!.Category,
                Scope = SettingScope.Tenant,
                Values = new Dictionary<string, string> { [Key] = "true", [SecondKey] = "true" }
            };
            var prepared = await session.Preparation.PrepareAsync(original,
                original.Values.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            return await ExecuteAsync(session, prepared, [Key, SecondKey], async token =>
            {
                var writer = Writer(session);
                var result = await writer.ApplyCurrentAsync(ValueEffect(SettingScope.Tenant, scenario.Tenant, Key, "true"), token);
                await Assert.That(result.Applied).IsTrue();
                firstApplied = true;
                try
                {
                    await writer.ApplyCurrentAsync(ValueEffect(SettingScope.Tenant, scenario.Tenant, SecondKey, "false"), token);
                }
                catch (UnauthorizedAccessException)
                {
                    invalidEffectRejected = true;
                }
                return true;
            });
        }));
        await Assert.That(firstApplied).IsTrue();
        await Assert.That(invalidEffectRejected).IsTrue();
        await Assert.That(await ReadAsync(scenario)).IsEqualTo(before);
    }

    private static AtprotoFederationSettingsWriter Writer(AuthenticatedSettingsFixture.Session session) =>
        ActivatorUtilities.GetServiceOrCreateInstance<AtprotoFederationSettingsWriter>(session.Services);

    private static Task<T> ExecuteAsync<T>(AuthenticatedSettingsFixture.Session session,
        IPreparedHumanSettingsMutation prepared, IReadOnlyCollection<string> keys, Func<CancellationToken, Task<T>> operation) =>
        session.MutationLock.ExecuteOrderedGroupsAsync([keys],
            token => session.Preparation.ExecutePreparedAsync(prepared, (_, ownerToken) => operation(ownerToken), token));

    private static HumanSettingsMutation ValueEffect(SettingScope scope, Guid tenant, string key, string value) =>
        new(scope == SettingScope.Instance ? HumanSettingsOperation.InstanceSettings : HumanSettingsOperation.TenantSettings,
            scope, scope == SettingScope.Instance ? null : tenant, null, key, HumanSettingsMutationKind.SetValue, value);

    private async Task<Scenario> CreateScenarioAsync(bool initiallyLocked = false, bool missingTarget = false,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        // This is the existing disposable container reset, not a production database operation.
        await fixture.ResetAsync();
        var authentication = await AuthenticatedSettingsFixture.CreateAsync(OpenMigrator, fixture, interceptor);
        try
        {
            Guid tenant = Guid.CreateVersion7(), foreign = Guid.CreateVersion7();
            await using var seed = OpenMigrator();
            seed.Tenants.AddRange(Tenant(tenant, TenantStatusEnum.Active), Tenant(foreign, TenantStatusEnum.Suspended));
            await seed.SaveChangesAsync();
            foreach (var (key, value) in new[]
            {
                (Key, "false"), (SecondKey, "false"),
                (GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode, "\"downtime_only\"")
            })
            {
                var existing = await seed.Set<AtprotoFederationInstanceSetting>().SingleOrDefaultAsync(row => row.SettingKey == key);
                if (existing is null)
                    seed.Add(AtprotoFederationInstanceSetting.Create(key, value, false, CreatedAt, null));
                else
                {
                    existing.SetValue(value, CreatedAt, null);
                    existing.SetLock(false, CreatedAt, null);
                }
            }
            if (!missingTarget)
                seed.Add(AtprotoFederationTenantSetting.Create(tenant, Key, "false", initiallyLocked, CreatedAt, null));
            seed.AddRange(
                AtprotoFederationTenantSetting.Create(tenant, SecondKey, "false", false, CreatedAt, null),
                AtprotoFederationTenantSetting.Create(foreign, Key, "false", false, CreatedAt, null));
            var tenantIds = await seed.Tenants.Select(row => row.Id).ToArrayAsync();
            var revisions = await seed.Set<EventDiscoveryRevision>().IgnoreTenantFilter(IsolationProof).ToListAsync();
            foreach (Guid id in tenantIds)
            {
                var revision = revisions.SingleOrDefault(row => row.TenantId == id);
                if (revision is null)
                {
                    revision = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = id };
                    seed.Add(revision);
                }
                if (id == tenant || id == foreign)
                {
                    revision.IdentityEpoch = id == tenant ? 7 : 13;
                    revision.DisclosureEpoch = id == tenant ? 11 : 17;
                }
            }
            await seed.SaveChangesAsync();
            return new(authentication, tenant, foreign);
        }
        catch
        {
            await authentication.DisposeAsync();
            throw;
        }
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private async Task<DatabaseState> ReadAsync(Scenario scenario)
    {
        await using var observer = OpenMigrator();
        var instances = await observer.Set<AtprotoFederationInstanceSetting>().AsNoTracking().ToListAsync();
        var tenants = await observer.Set<AtprotoFederationTenantSetting>().IgnoreTenantFilter(IsolationProof)
            .AsNoTracking().Where(row => row.TenantId == scenario.Tenant || row.TenantId == scenario.ForeignTenant).ToListAsync();
        var revisions = await observer.Set<EventDiscoveryRevision>().IgnoreTenantFilter(IsolationProof).AsNoTracking()
            .Where(row => row.TenantId == scenario.Tenant || row.TenantId == scenario.ForeignTenant).ToListAsync();
        return new(
            Stored(instances.Single(row => row.SettingKey == Key)),
            Stored(instances.Single(row => row.SettingKey == SecondKey)),
            tenants.SingleOrDefault(row => row.TenantId == scenario.Tenant && row.SettingKey == Key) is { } target ? Stored(target) : null,
            Stored(tenants.Single(row => row.TenantId == scenario.Tenant && row.SettingKey == SecondKey)),
            Stored(tenants.Single(row => row.TenantId == scenario.ForeignTenant && row.SettingKey == Key)),
            Revision(revisions.Single(row => row.TenantId == scenario.Tenant)),
            Revision(revisions.Single(row => row.TenantId == scenario.ForeignTenant)),
            await observer.SystemSettings.CountAsync(row => row.SettingKey == Key || row.SettingKey == SecondKey),
            await observer.Set<TenantSetting>().IgnoreTenantFilter(IsolationProof)
                .CountAsync(row => row.SettingKey == Key || row.SettingKey == SecondKey));
    }

    private static async Task AssertEpochsAsync(DatabaseState before, DatabaseState after, bool root = false)
    {
        await Assert.That(after.TargetRevision).IsEqualTo(before.TargetRevision with { DisclosureEpoch = before.TargetRevision.DisclosureEpoch + 1 });
        await Assert.That(after.ForeignRevision).IsEqualTo(root
            ? before.ForeignRevision with { DisclosureEpoch = before.ForeignRevision.DisclosureEpoch + 1 }
            : before.ForeignRevision);
    }

    private static async Task AssertLegacyUnchangedAsync(DatabaseState before, DatabaseState after)
    {
        await Assert.That(before.LegacySystemCount).IsEqualTo(0);
        await Assert.That(before.LegacyTenantCount).IsEqualTo(0);
        await Assert.That(after.LegacySystemCount).IsEqualTo(before.LegacySystemCount);
        await Assert.That(after.LegacyTenantCount).IsEqualTo(before.LegacyTenantCount);
    }

    private static Tenant Tenant(Guid id, TenantStatusEnum status) => new()
    {
        Id = id,
        FullName = "Federation mutation fixture",
        Slug = $"federation-mutation-{id:N}",
        TenantStatusId = (int)status,
        TenantStatus = null!
    };

    private static StoredSetting Stored(AtprotoFederationInstanceSetting row) =>
        new(row.Id, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
    private static StoredSetting Stored(AtprotoFederationTenantSetting row) =>
        new(row.Id, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
    private static RevisionState Revision(EventDiscoveryRevision row) => new(row.Id, row.IdentityEpoch, row.DisclosureEpoch);
    private sealed record StoredSetting(Guid Id, string Value, bool IsLocked, DateTime CreatedAt,
        Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    private sealed record RevisionState(Guid Id, long IdentityEpoch, long DisclosureEpoch);
    private sealed record DatabaseState(StoredSetting Instance, StoredSetting InstanceBackfill, StoredSetting? Target,
        StoredSetting TargetBackfill, StoredSetting Foreign, RevisionState TargetRevision, RevisionState ForeignRevision,
        int LegacySystemCount, int LegacyTenantCount);
    private sealed record Scenario(AuthenticatedSettingsFixture Authentication, Guid Tenant, Guid ForeignTenant) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Authentication.DisposeAsync();
    }
    private sealed class DeliberateRollbackException : Exception { }
}
