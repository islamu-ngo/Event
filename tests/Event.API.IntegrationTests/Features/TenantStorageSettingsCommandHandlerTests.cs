using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Category(TestCategories.Fast)]
    [Category("TenantStorageSettings")]
    public async Task Handle_WhenTenantStorageIsLocked_ReturnsFailureWithoutApplyingSettings()
    {
        await using var scenario = await TenantStorageMigrationScenario.CreateAsync(locked: true);
        var result = await scenario.Handler.ExecuteAsync(scenario.Command(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("StorageTenantOverridesLocked");
        await scenario.AssertRejectedAsync();
    }

    [Test]
    [Category(TestCategories.Fast)]
    [Category("TenantStorageSettings")]
    public async Task Handle_WhenMaxUploadExceedsInstanceCeiling_ReturnsValidationFailure()
    {
        await using var scenario = await TenantStorageMigrationScenario.CreateAsync(ceiling: 100);
        var result = await scenario.Handler.ExecuteAsync(scenario.Command(101), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).IsEqualTo("Tenant storage settings validation failed.");
        await Assert.That(result.Errors!.Any(error => error.Contains("instance ceiling", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await scenario.AssertRejectedAsync();
    }

    [Test]
    [Category(TestCategories.Fast)]
    [Category("TenantStorageSettings")]
    public async Task Handle_WhenTenantAdminTargetsDifferentTenant_ReturnsForbiddenFailure()
    {
        await using var scenario = await TenantStorageMigrationScenario.CreateAsync(otherTenant: true);
        var result = await scenario.Handler.ExecuteAsync(scenario.Command(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("Only tenant administrators");
        await Assert.That(scenario.Observation.ReadCount).IsEqualTo(0);
        await scenario.AssertRejectedAsync();
    }

    [Test]
    [Category(TestCategories.Fast)]
    [Category("TenantStorageSettings")]
    public async Task Handle_WhenPatchHasNoSuppliedLeaves_ReturnsValidationFailureWithoutMutation()
    {
        await using var scenario = await TenantStorageMigrationScenario.CreateAsync();
        var command = scenario.Command() with { Settings = new PatchTenantStorageSettingsDto() };
        var result = await scenario.Handler.ExecuteAsync(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!.Any(error => error.Contains("at least one", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await scenario.AssertRejectedAsync();
    }

    [Test]
    [Category(TestCategories.Fast)]
    [Category("TenantStorageSettings")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Handle_WhenTransactionCommits_InvalidatesCachesOnceAfterCommit(bool platformAdministrator)
    {
        await using var scenario = await TenantStorageMigrationScenario.CreateAsync(platformAdministrator: platformAdministrator);
        var command = scenario.Command(50);
        var result = await scenario.Handler.ExecuteAsync(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(scenario.Transaction.ExecutionCount).IsEqualTo(1);
        await Assert.That(scenario.Observation.MutationCount).IsEqualTo(1);
        await Assert.That(ReferenceEquals(scenario.Observation.OriginalCommand, command)).IsTrue();
        await Assert.That(scenario.Calls.Count).IsEqualTo(4);
        await Assert.That(scenario.Calls[0]).IsEqualTo("persisted");
        await Assert.That(scenario.Calls[1]).IsEqualTo("committed");
        await Assert.That(scenario.Calls[2]).IsEqualTo("hierarchical-invalidated");
        await Assert.That(scenario.Calls[3]).IsEqualTo("s3-invalidated");
        await scenario.AssertPersistedAsync(committed: true);
    }

    [Test]
    [Category(TestCategories.Fast)]
    [Category("TenantStorageSettings")]
    public async Task Handle_WhenTransactionFails_DoesNotInvalidateCaches()
    {
        await using var scenario = await TenantStorageMigrationScenario.CreateAsync(failCommit: true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scenario.Handler.ExecuteAsync(scenario.Command(50), CancellationToken.None));

        await Assert.That(scenario.Transaction.ExecutionCount).IsEqualTo(1);
        await Assert.That(scenario.Calls.Count).IsEqualTo(1);
        await Assert.That(scenario.Calls[0]).IsEqualTo("persisted");
        await Assert.That(scenario.Observation.MutationCount).IsEqualTo(1);
        await scenario.AssertPersistedAsync(committed: false);
    }

    /// <summary>Owns only this fixture's signed request, persisted grants and selected Runtime owner.</summary>
    private sealed class TenantStorageMigrationScenario(
        LocalIdentityLifecycleHttpFixture fixture, Request request, IPolicyMutationExecutionScope selected,
        TenantStorageTransactionObservation transaction, List<string> calls) : IAsyncDisposable
    {
        private static readonly Guid OtherTenant = Guid.Parse("018e4e5c-7f00-7000-8000-000000000099");
        internal List<string> Calls => calls;
        internal TenantStorageTransactionObservation Transaction => transaction;
        internal TenantStorageServiceObservation Observation { get; private set; } = null!;
        internal PatchTenantStorageSettingsCommandHandler Handler { get; private set; } = null!;

        internal static async Task<TenantStorageMigrationScenario> CreateAsync(
            bool locked = false, long ceiling = 128, bool otherTenant = false,
            bool failCommit = false, bool platformAdministrator = false)
        {
            var calls = new List<string>();
            var transaction = new TenantStorageTransactionObservation(calls, failCommit);
            LocalIdentityLifecycleHttpFixture? fixtureReference = null;
            var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
                interceptor: transaction,
                configureServices: registrations =>
                {
                    ObserveStorageWriterOwner(registrations, owner => transaction.Owner = owner);
                    registrations.AddScoped<TenantStorageActivationObservation>(owner => new(
                        ActivatorUtilities.CreateInstance<HierarchicalSettingsResolver>(owner),
                        ActivatorUtilities.CreateInstance<S3ConfigResolver>(owner),
                        owner.GetRequiredService<ExploreDbContext>(),
                        () => fixtureReference ?? throw new InvalidOperationException("The signed fixture is not ready."),
                        calls));
                    registrations.AddScoped<IHierarchicalSettingsResolver>(
                        owner => owner.GetRequiredService<TenantStorageActivationObservation>());
                    registrations.AddScoped<IS3ConfigResolver>(
                        owner => owner.GetRequiredService<TenantStorageActivationObservation>());
                });
            fixtureReference = fixture;
            Request? request = null;
            IPolicyMutationExecutionScope? selected = null;
            try
            {
                if (platformAdministrator) await SeedPlatformAdministratorAsync(fixture);
                await using (var seed = fixture.Native.CreateDatabase())
                {
                    if (!platformAdministrator)
                    {
                        var member = new TenantUser
                        {
                            Id = Guid.CreateVersion7(),
                            TenantId = PlatformDefaults.DefaultTenantId,
                            Tenant = null!,
                            UserId = fixture.Binding.LocalSubjectId,
                            User = null!,
                            StatusId = (int)TenantUserStatusEnum.Active,
                            JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                        };
                        seed.TenantUsers.Add(member);
                        seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                        {
                            Id = Guid.CreateVersion7(),
                            TenantId = member.TenantId,
                            Tenant = null!,
                            TenantUserId = member.Id,
                            TenantUser = null!,
                            RoleId = (int)RoleEnum.TenantAdmin,
                            Role = null!,
                            RoleScopeId = (int)RoleScopeEnum.Tenant,
                            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
                        });
                    }
                    if (otherTenant)
                        seed.Tenants.Add(new Tenant
                        {
                            Id = OtherTenant,
                            FullName = "Other storage tenant",
                            Slug = "other-storage",
                            TenantStatusId = (int)TenantStatusEnum.Active,
                            TenantStatus = await seed.Set<TenantStatus>()
                                .SingleAsync(status => status.Id == (int)TenantStatusEnum.Active),
                            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                        });
                    foreach (var (key, value) in new Dictionary<string, string>
                    {
                        [GovernanceSettingKeys.Deployment.Mode] = "\"MultiTenant\"",
                        [GovernanceSettingKeys.TenantDelegation.LockStorage] = locked ? "true" : "false",
                        [GovernanceSettingKeys.Storage.Provider] = "\"local\"",
                        [GovernanceSettingKeys.Storage.DefaultMaxUploadBytes] = "64",
                        [GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes] = "1024",
                        [GovernanceSettingKeys.Storage.InstanceMaxUploadBytes] = ceiling.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    })
                    {
                        var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
                        if (row is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = value });
                        else row.Value = value;
                    }
                    seed.Set<TenantSetting>().AddRange(
                        new TenantSetting
                        {
                            TenantId = PlatformDefaults.DefaultTenantId,
                            Tenant = null!,
                            SettingKey = GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
                            Value = "64",
                            IsLocked = true
                        },
                        new TenantSetting
                        {
                            TenantId = PlatformDefaults.DefaultTenantId,
                            Tenant = null!,
                            SettingKey = GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
                            Value = "1024"
                        },
                        new TenantSetting
                        {
                            TenantId = PlatformDefaults.DefaultTenantId,
                            Tenant = null!,
                            SettingKey = GovernanceSettingKeys.Storage.BucketName,
                            Value = "\"kept-bucket\"",
                            IsLocked = true
                        });
                    await seed.SaveChangesAsync();
                }
                request = await Request.CreateAsync(fixture);
                Guid target = otherTenant ? OtherTenant : PlatformDefaults.DefaultTenantId;
                request.Services.GetRequiredService<ITenantContextAccessor>().SetTenant(target);
                selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                    .CreateAsync(new(target), CancellationToken.None);
                var services = selected.Services;
                var context = services.GetRequiredService<ExploreDbContext>();
                transaction.Owner = context;
                var scenario = new TenantStorageMigrationScenario(fixture, request, selected, transaction, calls)
                {
                    Observation = new(services.GetRequiredService<ITenantStorageSettingService>())
                };
                scenario.Handler = new(
                    services.GetRequiredService<ITenantContext>(), services.GetRequiredService<IAdminContext>(),
                    scenario.Observation);
                return scenario;
            }
            catch
            {
                if (selected is not null) await selected.DisposeAsync();
                if (request is not null) await request.DisposeAsync();
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal PatchTenantStorageSettingsCommand Command(long maxUploadBytes = 64) => new()
        {
            UserId = fixture.Binding.LocalSubjectId,
            Settings = new()
            {
                Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(maxUploadBytes) }
            }
        };

        internal async Task AssertRejectedAsync()
        {
            await Assert.That(Observation.MutationCount).IsEqualTo(0);
            await Assert.That(Transaction.ExecutionCount).IsEqualTo(0);
            await Assert.That(Calls.Count).IsEqualTo(0);
            await AssertPersistedAsync(committed: false);
        }

        internal async Task AssertPersistedAsync(bool committed)
        {
            await using var observer = fixture.Native.CreateDatabase();
            var rows = await observer.Set<TenantSetting>().AsNoTracking().ToListAsync();
            await Assert.That(rows.Count).IsEqualTo(3);
            await Assert.That(rows.All(row => row.TenantId == PlatformDefaults.DefaultTenantId)).IsTrue();
            var upload = rows.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes);
            await Assert.That(upload.Value).IsEqualTo(committed ? "50" : "64");
            await Assert.That(upload.IsLocked).IsTrue();
            await Assert.That(upload.UpdatedBy).IsEqualTo(committed ? fixture.Binding.LocalSubjectId : (Guid?)null);
            var quota = rows.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes);
            await Assert.That(quota.Value).IsEqualTo("1024");
            await Assert.That(quota.IsLocked).IsFalse();
            await Assert.That(quota.UpdatedBy).IsNull();
            var bucket = rows.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName);
            await Assert.That(bucket.Value).IsEqualTo("\"kept-bucket\"");
            await Assert.That(bucket.IsLocked).IsTrue();
            await Assert.That(bucket.UpdatedBy).IsNull();
            await Assert.That(await observer.SystemSettings.Where(row =>
                    row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes)
                .Select(row => row.Value).SingleAsync()).IsEqualTo("64");
        }

        public async ValueTask DisposeAsync()
        {
            await selected.DisposeAsync();
            await request.DisposeAsync();
            await fixture.DisposeAsync();
        }
    }

    /// <summary>Observes entrypoints while delegating every operation to the real storage service.</summary>
    private sealed class TenantStorageServiceObservation(ITenantStorageSettingService inner) : ITenantStorageSettingService
    {
        internal int ReadCount { get; private set; }
        internal int MutationCount { get; private set; }
        internal PatchTenantStorageSettingsCommand? OriginalCommand { get; private set; }
        public Task<TenantStorageSettingsDto> ReadSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return inner.ReadSettingsAsync(tenantId, cancellationToken);
        }
        public Task<StorageSettingsMutation> ObserveConfigurationAsync(
            PatchTenantStorageSettingsCommand command, CancellationToken cancellationToken = default)
            => inner.ObserveConfigurationAsync(command, cancellationToken);
        public Task ApplyPatchAsync(StorageSettingsMutation observation, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            OriginalCommand = (PatchTenantStorageSettingsCommand)observation.OriginalCommand;
            return inner.ApplyPatchAsync(observation, cancellationToken);
        }
        public Task<InstanceStorageProviderStatusDto> TestProviderAsync(TestTenantStorageProviderCommand command, CancellationToken cancellationToken = default)
            => inner.TestProviderAsync(command, cancellationToken);
    }

    private sealed class TenantStorageTransactionObservation(List<string> calls, bool failCommit) : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal int ExecutionCount { get; private set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner)) ExecutionCount++;
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner))
            {
                var value = await Owner!.Set<TenantSetting>().AsNoTracking().Where(row =>
                        row.TenantId == PlatformDefaults.DefaultTenantId
                        && row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes)
                    .Select(row => row.Value).SingleAsync(cancellationToken);
                if (value != "50") throw new InvalidOperationException("The storage leaf did not reach the owning transaction.");
                calls.Add("persisted");
                if (failCommit) throw new InvalidOperationException("Rejected physical storage settlement.");
            }
            return result;
        }
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner)) calls.Add("committed");
            return Task.CompletedTask;
        }
    }

    /// <summary>Delegates real invalidation and requires independently visible settlement before activation.</summary>
    private sealed class TenantStorageActivationObservation(
        IHierarchicalSettingsResolver settings, IS3ConfigResolver s3, ExploreDbContext owner,
        Func<LocalIdentityLifecycleHttpFixture> fixture, List<string> calls) : IHierarchicalSettingsResolver, IS3ConfigResolver
    {
        private void RequireSettled()
        {
            if (owner.Database.CurrentTransaction is not null || calls.LastOrDefault() is not
                ("committed" or "hierarchical-invalidated"))
                throw new InvalidOperationException("Storage activation preceded physical settlement.");
            using var independent = fixture().Native.CreateDatabase();
            var row = independent.Set<TenantSetting>().AsNoTracking().Single(item =>
                item.TenantId == PlatformDefaults.DefaultTenantId
                && item.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes);
            if (row.Value != "50" || row.UpdatedBy != fixture().Binding.LocalSubjectId)
                throw new InvalidOperationException("Activation could not independently observe the original actor's effect.");
        }
        public void InvalidateCache(SettingScope? scope = null, Guid? scopeId = null)
        {
            if (scope != SettingScope.Tenant || scopeId != PlatformDefaults.DefaultTenantId)
                throw new InvalidOperationException("Storage invalidation escaped the selected tenant.");
            RequireSettled();
            calls.Add("hierarchical-invalidated");
            settings.InvalidateCache(scope, scopeId);
        }
        void IS3ConfigResolver.InvalidateCache(Guid? tenantId)
        {
            if (tenantId != PlatformDefaults.DefaultTenantId)
                throw new InvalidOperationException("S3 invalidation escaped the selected tenant.");
            RequireSettled();
            calls.Add("s3-invalidated");
            s3.InvalidateCache(tenantId);
        }
        public Task<S3Configuration?> ResolveAsync(CancellationToken cancellationToken = default)
            => s3.ResolveAsync(cancellationToken);
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
            => s3.IsConfiguredAsync(cancellationToken);
        public Task<T?> ResolveAsync<T>(string key, SettingContext context, CancellationToken ct = default)
            => settings.ResolveAsync<T>(key, context, ct);
        public Task<ResolvedSetting?> ResolveWithMetadataAsync(string key, SettingContext context, CancellationToken ct = default)
            => settings.ResolveWithMetadataAsync(key, context, ct);
        public Task<IReadOnlyList<ResolvedSetting>> ResolveBatchAsync(
            IEnumerable<string> keys, SettingContext context, CancellationToken ct = default)
            => settings.ResolveBatchAsync(keys, context, ct);
        public Task<TGroup> ResolveGroupAsync<TGroup>(SettingContext context, CancellationToken ct = default)
            where TGroup : ISettingGroup, new() => settings.ResolveGroupAsync<TGroup>(context, ct);
        public void InvalidateOrganizationCache(Guid tenantId, Guid organizationId)
            => settings.InvalidateOrganizationCache(tenantId, organizationId);
        public void InvalidateUserCache(Guid tenantId, Guid userId) => settings.InvalidateUserCache(tenantId, userId);
        public Task SetValueAsync(string key, string value, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
            => settings.SetValueAsync(key, value, scope, scopeId, actorId, ct);
        public Task RemoveOverrideAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
            => settings.RemoveOverrideAsync(key, scope, scopeId, actorId, ct);
        public Task LockAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
            => settings.LockAsync(key, scope, scopeId, actorId, ct);
        public Task UnlockAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
            => settings.UnlockAsync(key, scope, scopeId, actorId, ct);
    }
}
