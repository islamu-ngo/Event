using System.Collections.Immutable;
using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    private static readonly DateTime PublicationTime = new(2026, 8, 25, 12, 34, 56, DateTimeKind.Utc);
    private static string IntakeKey => EventReportingIntakeSettingDefinitions.IntakeEnabled.Key;
    private static string ApprovalKey => EventSettingDefinitions.RequireApproval.Key;
    private static string UserSubmissionKey => EventSettingDefinitions.UserSubmissionEnabled.Key;
    private static string OrganizationSubmissionKey => EventSettingDefinitions.OrganizationSubmissionEnabled.Key;
    private static string GroupSubmissionKey => EventSettingDefinitions.GroupSubmissionEnabled.Key;

    private static WebApplicationFactory<Program> CreateMigratedSettingsHost(
        LocalIdentityLifecycleHttpFixture identity, PublicationObservation? observation = null) =>
        CreateDataBoundaryHost(identity).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // The ordinary fixture's factory configures the actual database and
            // interceptors. Selection still belongs to the production scope factory;
            // its retained context must be the one resolved by every source and store.
            var contexts = new SettingsObservationContexts(identity.Native);
            services.RemoveAll<IGlobalExploreDbContextFactory>();
            services.AddSingleton<IGlobalExploreDbContextFactory>(contexts);
            services.RemoveAll<ITenantExploreDbContextFactory>();
            services.AddSingleton<ITenantExploreDbContextFactory>(contexts);
            services.AddScoped(provider =>
            {
                if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                    return selected;
                var context = identity.Native.CreateDatabase();
                context.TenantContext = provider.GetRequiredService<ITenantContext>();
                context.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                context.ClearTenantFilterBypass();
                return context;
            });
            if (observation is not null)
            {
                services.RemoveAll<ISettingMutationLock>();
                services.AddScoped<ISettingMutationLock>(provider => new ObservedPublicationLock(
                    new RelationalSettingMutationLock(provider.GetRequiredService<ExploreDbContext>(),
                        provider.GetRequiredService<IUnitOfWork>(), (key, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            if (observation.FailurePhase == "lock")
                                throw observation.Failure!;
                            return Task.CompletedTask;
                        }), observation));
            }
        }));

    private sealed class SettingsObservationContexts(LocalAdmissionWebApplicationFactory database)
        : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Open(null, cancellationToken);
        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Open(tenantId != Guid.Empty ? tenantId : throw new ArgumentException("An exact tenant is required."), cancellationToken);
        private Task<ExploreDbContext> Open(Guid? tenant, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var context = database.CreateDatabase();
            context.ClearTenantFilterBypass();
            context.TenantContext = tenant is { } target ? new ObservedTenant(target) : null;
            context.CurrentUserService = null;
            context.BindTenantLease();
            return Task.FromResult(context);
        }
        private sealed record ObservedTenant(Guid TenantId) : ITenantContext;
    }

    private sealed class PublicationFixture : IAsyncDisposable
    {
        internal LocalIdentityLifecycleHttpFixture Identity { get; private init; } = null!;
        internal WebApplicationFactory<Program> Host { get; private init; } = null!;
        internal Request Request { get; private init; } = null!;
        internal PublicationObservation Observation { get; private init; } = new();
        internal Guid Actor => Identity.Binding.LocalSubjectId;
        internal Guid Tenant => PlatformDefaults.DefaultTenantId;

        internal static async Task<PublicationFixture> CreateAsync()
        {
            var observation = new PublicationObservation();
            var identity = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: observation);
            await SeedPlatformAdministratorAsync(identity);
            var host = CreateMigratedSettingsHost(identity, observation);
            var request = await CreateDataBoundaryRequestAsync(identity, host);
            var fixture = new PublicationFixture
            {
                Identity = identity,
                Host = host,
                Request = request,
                Observation = observation
            };
            await using var seed = identity.Native.CreateDatabase();
            foreach (string key in PublicationPolicySettingKeys.All)
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
                string value = key == ApprovalKey ? "false" : "true";
                if (row is null)
                    seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
                else
                {
                    row.Value = value;
                    row.IsLocked = false;
                }
            }
            await seed.SaveChangesAsync();
            return fixture;
        }

        internal async Task SeedSystemAsync(string key, string value, bool locked = false)
        {
            await using var seed = Identity.Native.CreateDatabase();
            var row = await seed.SystemSettings.SingleAsync(item => item.SettingKey == key);
            row.Value = value;
            row.IsLocked = locked;
            await seed.SaveChangesAsync();
        }

        internal async Task SeedTenantAsync(string key, string value, Guid? tenant = null)
        {
            await using var seed = Identity.Native.CreateDatabase();
            Guid target = tenant ?? Tenant;
            var row = await seed.TenantSettingOverrides.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.TenantId == target && item.SettingKey == key);
            if (row is null)
                seed.TenantSettingOverrides.Add(new TenantSetting
                {
                    TenantId = target,
                    Tenant = null!,
                    SettingKey = key,
                    Value = value,
                    CreatedBy = Actor,
                    CreatedAt = PublicationTime
                });
            else
                row.Value = value;
            await seed.SaveChangesAsync();
        }

        internal async Task<string?> ReadAsync(bool instance, string key)
        {
            await using var observer = Identity.Native.CreateDatabase();
            return instance
                ? await observer.SystemSettings.Where(row => row.SettingKey == key)
                    .Select(row => row.Value).SingleOrDefaultAsync()
                : await observer.TenantSettingOverrides.Where(row => row.TenantId == Tenant && row.SettingKey == key)
                    .Select(row => row.Value).SingleOrDefaultAsync();
        }

        internal async Task<PublicationPolicyMutationResult> ApplyAsync(bool instance,
            ImmutableDictionary<string, string> values,
            PublicationPolicyLockedSystemBehavior behavior = PublicationPolicyLockedSystemBehavior.Reject,
            bool callerLock = false, CancellationToken cancellationToken = default,
            Func<ExploreDbContext, PublicationPolicyMutationResult, CancellationToken, Task>? afterWrite = null)
        {
            await using var selected = await Request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(instance ? PolicyMutationScope.Instance : new(Tenant), cancellationToken);
            var services = selected.Services;
            var context = services.GetRequiredService<ExploreDbContext>();
            var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var command = new UpdateSettingBatchCommand
            {
                Category = "events",
                Scope = instance ? SettingScope.Instance : SettingScope.Tenant,
                Values = values
            };
            var proof = await preparation.PrepareAsync(command, values, cancellationToken);
            var boundary = services.GetRequiredService<IPublicationPolicyMutationBoundary>();
            var mutationLock = services.GetRequiredService<ISettingMutationLock>();
            var mutations = values.Select(pair => new PublicationPolicySettingMutation(pair.Key,
                PublicationPolicyMutationKind.Set, pair.Value, instance ? null : Tenant,
                instance ? false : null)).ToImmutableArray();
            Observation.Begin(context);
            try
            {
                Task<PublicationPolicyMutationResult> Apply(CancellationToken token) =>
                    instance
                        ? callerLock
                            ? boundary.ApplyInstanceInCurrentTransactionAsync(new(Actor, PublicationTime, mutations), token)
                            : boundary.ApplyInstanceAsync(new(Actor, PublicationTime, mutations), token)
                        : callerLock
                            ? boundary.ApplyTenantInCurrentTransactionAsync(new(Tenant, Actor, PublicationTime, mutations, behavior), token)
                            : boundary.ApplyTenantAsync(new(Tenant, Actor, PublicationTime, mutations, behavior), token);
                async Task<PublicationPolicyMutationResult> Execute(CancellationToken token) =>
                    await preparation.ExecutePreparedAsync(proof, async (_, ownerToken) =>
                    {
                        var result = callerLock
                            ? await mutationLock.ExecuteManyAsync(PublicationPolicySettingKeys.All, Apply, ownerToken)
                            : await Apply(ownerToken);
                        if (afterWrite is not null)
                            await afterWrite(context, result, ownerToken);
                        return result;
                    }, token);
                return Observation.FailurePhase == "lock"
                    ? await mutationLock.ExecuteOrderedGroupsAsync([PublicationPolicySettingKeys.All], Execute, cancellationToken)
                    : await Execute(cancellationToken);
            }
            finally
            {
                Observation.Owner = null;
                await Assert.That(context.Database.CurrentTransaction).IsNull();
                await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
            }
        }

        internal async Task<T> ExecuteContractAsync<T>(PolicyMutationScope scope,
            Func<IServiceProvider, CancellationToken, Task<IPreparedHumanSettingsMutation>> prepare,
            Func<IServiceProvider, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            await using var selected = await Request.Services
                .GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(scope, cancellationToken);
            var services = selected.Services;
            var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var proof = await prepare(services, cancellationToken);
            var context = services.GetRequiredService<ExploreDbContext>();
            Observation.Begin(context);
            try
            {
                return await preparation.ExecutePreparedAsync(proof,
                    (_, token) => action(services, token), cancellationToken);
            }
            finally
            {
                Observation.Owner = null;
                await Assert.That(context.Database.CurrentTransaction).IsNull();
                await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Request.DisposeAsync();
            await Host.DisposeAsync();
            await Identity.DisposeAsync();
        }
    }

    // Observation delegates every lock to the real relational implementation. It does
    // not acquire an owner, issue proofs, replay callbacks, or synthesize store results.
    private sealed class ObservedPublicationLock(ISettingMutationLock inner, PublicationObservation observation)
        : ISettingMutationLock
    {
        public Task<T> ExecuteAsync<T>(string key, Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) => inner.ExecuteAsync(key, action, cancellationToken);

        public async Task<T> ExecuteManyAsync<T>(IEnumerable<string> keys, Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            string[] captured = keys.ToArray();
            if (observation.Owner is null)
                return await inner.ExecuteManyAsync(captured, action, cancellationToken);
            observation.LockKeys.Add(captured);
            observation.Tokens.Add(cancellationToken);
            return await inner.ExecuteManyAsync(captured, async token =>
            {
                observation.Trace.Add("lock-enter");
                var result = await action(token);
                observation.Trace.Add("lock-exit");
                return result;
            }, cancellationToken);
        }

        public Task<T> ExecuteOrderedGroupsAsync<T>(IEnumerable<IEnumerable<string>> groups,
            Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            inner.ExecuteOrderedGroupsAsync(groups, action, cancellationToken);
    }

    private sealed class PublicationObservation : DbCommandInterceptor, ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal string? FailurePhase { get; set; }
        internal Exception? Failure { get; set; }
        internal List<string> Trace { get; } = [];
        internal List<string[]> LockKeys { get; } = [];
        internal List<CancellationToken> Tokens { get; } = [];
        internal List<(string Key, EntityState State, string Value)> Effects { get; } = [];
        internal int Saves { get; private set; }
        internal int Commits { get; private set; }
        internal int Starts { get; private set; }

        internal void Begin(ExploreDbContext owner)
        {
            Owner = owner;
            Trace.Clear();
            LockKeys.Clear();
            Tokens.Clear();
            Effects.Clear();
            Saves = Commits = Starts = 0;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner)
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && (command.CommandText.Contains("system_settings", StringComparison.Ordinal)
                    || command.CommandText.Contains("tenant_setting_overrides", StringComparison.Ordinal)))
            {
                Tokens.Add(cancellationToken);
                if (Trace.Contains("lock-enter"))
                {
                    Trace.Add("read");
                    if (FailurePhase == "read")
                        throw Failure!;
                }
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, Owner))
                return ValueTask.FromResult(result);
            var changes = Owner!.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Where(entry => entry.Entity is SystemSetting system && PublicationPolicySettingKeys.All.Contains(system.SettingKey)
                    || entry.Entity is TenantSetting tenant && PublicationPolicySettingKeys.All.Contains(tenant.SettingKey))
                .ToArray();
            if (changes.Length == 0)
                return ValueTask.FromResult(result);
            Saves++;
            Trace.Add("saving");
            Tokens.Add(cancellationToken);
            foreach (var entry in changes)
            {
                if (entry.Entity is SystemSetting system)
                    Effects.Add((system.SettingKey, entry.State, system.Value));
                else if (entry.Entity is TenantSetting tenant)
                    Effects.Add((tenant.SettingKey, entry.State, tenant.Value));
            }
            if (FailurePhase == "saving")
                throw Failure!;
            return ValueTask.FromResult(result);
        }

        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner) && Saves > 0)
            {
                Trace.Add("saved");
                if (FailurePhase == "saved")
                    throw Failure!;
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner))
                Starts++;
            return ValueTask.FromResult(result);
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner))
            {
                Commits++;
                Trace.Add("commit");
            }
            return Task.CompletedTask;
        }
    }
}
