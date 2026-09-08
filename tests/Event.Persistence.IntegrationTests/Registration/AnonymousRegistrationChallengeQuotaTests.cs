// ABOUTME: Proves durable challenge budgets using real SQLite stores, transactions, scope constraints and replica barriers.
// ABOUTME: Replaces only the external database-clock scalar; no quota, repository, UoW or internal lock mocks.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Models;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Registration;

public sealed class AnonymousRegistrationChallengeQuotaTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Replicas_ContendAtNativeTransactionBoundary_WithoutProcessLock(bool sameEvent)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var first = await fixture.SeedEventAsync();
        var second = sameEvent ? first : await fixture.SeedEventAsync();
        await LimitsAsync(fixture.Context, sameEvent ? "10" : "1", sameEvent ? "1" : "10");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var holder = new TransactionBarrier(holdAfterStart: true);
        var contender = new TransactionBarrier(holdAfterStart: false);
        await using var left = Context(fixture, fixture.TenantId, holder);
        await using var right = Context(fixture, fixture.TenantId, contender);
        Task<bool> leftResult = Task.Run(() => AcquireAsync(left, fixture.TenantId, first.Id, timeout.Token));
        await holder.Started.Task.WaitAsync(timeout.Token);
        Task<bool> rightResult = Task.Run(() => AcquireAsync(right, fixture.TenantId, second.Id, timeout.Token));
        try
        {
            await contender.Starting.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            holder.Release.TrySetResult();
        }
        bool[] results = await Task.WhenAll(leftResult, rightResult).WaitAsync(timeout.Token);
        await Assert.That(results.Count(value => value)).IsEqualTo(1);
        await Assert.That(await fixture.Context.Set<AnonymousChallengeTenantQuota>().Select(row => row.Issued).SingleAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.Set<AnonymousChallengeEventQuota>().SumAsync(row => row.Issued)).IsEqualTo(1);
        await Assert.That(await fixture.Context.Set<AnonymousChallengeEventQuota>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task EventRejection_RollsBackTenantCharge_AndOuterFailureRollsBackBoth()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var first = await fixture.SeedEventAsync();
        var second = await fixture.SeedEventAsync();
        await LimitsAsync(fixture.Context, "10", "1");
        await using var context = Context(fixture, fixture.TenantId);
        await Assert.That(await AcquireAsync(context, fixture.TenantId, first.Id)).IsTrue();
        await Assert.That(await AcquireAsync(context, fixture.TenantId, first.Id)).IsFalse();
        await Assert.That(await context.Set<AnonymousChallengeTenantQuota>().Select(row => row.Issued).SingleAsync()).IsEqualTo(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfCoreUnitOfWork(context).ExecuteBootstrapConvergenceAsync<bool>(async token =>
        {
            await Assert.That(await new AnonymousRegistrationChallengeQuota(context).TryAcquireAsync(fixture.TenantId, second.Id, token)).IsTrue();
            throw new InvalidOperationException("Issuer failed after both quota charges.");
        }));
        await Assert.That(await context.Set<AnonymousChallengeTenantQuota>().Select(row => row.Issued).SingleAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<AnonymousChallengeEventQuota>().CountAsync()).IsEqualTo(1);
        await Assert.That(await AcquireAsync(context, fixture.TenantId, second.Id)).IsTrue();
    }

    [Test]
    public async Task DatabaseMinuteRollover_ReusesRows_AndBackwardTimeDoesNotResetBudget()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync();
        await LimitsAsync(fixture.Context, "1", "1");
        var clock = new DatabaseClock();
        await using var context = Context(fixture, fixture.TenantId, clock);
        await Assert.That(await AcquireAsync(context, fixture.TenantId, entity.Id)).IsTrue();
        await Assert.That(await AcquireAsync(context, fixture.TenantId, entity.Id)).IsFalse();
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await Assert.That(await AcquireAsync(context, fixture.TenantId, entity.Id)).IsTrue();
        clock.UtcNow = clock.UtcNow.AddMinutes(-1);
        await Assert.That(await AcquireAsync(context, fixture.TenantId, entity.Id)).IsFalse();
        await Assert.That(await context.Set<AnonymousChallengeTenantQuota>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<AnonymousChallengeEventQuota>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<AnonymousChallengeEventQuota>().Select(row => row.Issued).SingleAsync()).IsEqualTo(1);
        await Assert.That(clock.Reads).IsEqualTo(4);
    }

    [Test]
    public async Task ScopeQualification_RejectsUnknownAndCrossTenantEvents_WithoutCreatingCounters()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var first = await fixture.SeedEventAsync();
        Guid otherTenant = Guid.CreateVersion7();
        Guid otherEvent = Guid.CreateVersion7();
        fixture.Context.Tenants.Add(new Tenant { Id = otherTenant, FullName = "Other quota tenant", Slug = $"quota-{otherTenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null! });
        fixture.Context.Events.Add(new Explore.Domain.Event { Id = otherEvent, TenantId = otherTenant, Tenant = null!,
            Title = "Other quota event", ActorId = fixture.ActorId, Actor = null!, OrganizerActorId = fixture.ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null! });
        await fixture.Context.SaveChangesAsync();
        await LimitsAsync(fixture.Context, "1", "1");
        await using var left = Context(fixture, fixture.TenantId);
        await using var right = Context(fixture, otherTenant);
        await Assert.That(await AcquireAsync(left, fixture.TenantId, Guid.CreateVersion7())).IsFalse();
        await Assert.That(await AcquireAsync(left, fixture.TenantId, otherEvent)).IsFalse();
        await Assert.That(await AcquireAsync(left, otherTenant, otherEvent)).IsFalse();
        await Assert.That(await left.Set<AnonymousChallengeTenantQuota>().CountAsync()).IsEqualTo(0);
        await Assert.That(await AcquireAsync(left, fixture.TenantId, first.Id)).IsTrue();
        await Assert.That(await AcquireAsync(right, otherTenant, otherEvent)).IsTrue();
        await Assert.That(await AcquireAsync(left, fixture.TenantId, first.Id)).IsFalse();
        await Assert.That(await left.Set<AnonymousChallengeTenantQuota>().CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task NativeConstraints_RejectDuplicateScope_CrossTenantEvent_AndInvalidCount()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync();
        await using var context = Context(fixture, fixture.TenantId);
        await Assert.That(await AcquireAsync(context, fixture.TenantId, entity.Id)).IsTrue();
        context.Add(new AnonymousChallengeEventQuota { TenantId = fixture.TenantId, EventId = entity.Id, WindowMinute = 1, Issued = 0 });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.Add(new AnonymousChallengeEventQuota { TenantId = Guid.CreateVersion7(), EventId = entity.Id, WindowMinute = 1, Issued = 0 });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => context.Set<AnonymousChallengeEventQuota>()
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Issued, -1)));
        await Assert.ThrowsAsync<SqliteException>(() => context.Set<AnonymousChallengeEventQuota>()
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Issued, 10001)));
        await Assert.That(await context.Set<AnonymousChallengeEventQuota>().Select(row => row.Issued).SingleAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task NativeRegistration_RequiresAmbientTransaction_AndInvalidSettingsFailClosed()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync();
        var quota = fixture.Services.GetRequiredService<IAnonymousRegistrationChallengeQuota>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => quota.TryAcquireAsync(fixture.TenantId, entity.Id, CancellationToken.None));
        await LimitsAsync(fixture.Context, "0", "1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => AcquireAsync(fixture.Context, fixture.TenantId, entity.Id));
        await Assert.That(await fixture.Context.Set<AnonymousChallengeTenantQuota>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Settings_AreUncached_AndHonorCanonicalDefaultsAndInstanceLocks()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync();
        await using var context = Context(fixture, fixture.TenantId);
        await Assert.That(await AcquireAsync(context, fixture.TenantId, entity.Id)).IsTrue();
        await LimitsAsync(fixture.Context, "1", "1", locked: true);
        fixture.Context.TenantSettingOverrides.Add(new TenantSetting { Id = Guid.CreateVersion7(), TenantId = fixture.TenantId,
            Tenant = null!, SettingKey = GovernanceSettingKeys.AnonymousRegistrationChallenge.TenantPermitsPerMinute, Value = "\"10000\"" });
        await fixture.Context.SaveChangesAsync();
        await Assert.That(await AcquireAsync(context, fixture.TenantId, entity.Id)).IsFalse();
        await Assert.That(SettingRegistry.Get(GovernanceSettingKeys.AnonymousRegistrationChallenge.TenantPermitsPerMinute)!.DefaultValue).IsEqualTo("\"600\"");
        await Assert.That(SettingRegistry.Get(GovernanceSettingKeys.AnonymousRegistrationChallenge.EventPermitsPerMinute)!.DefaultValue).IsEqualTo("\"120\"");
    }

    private static Task<bool> AcquireAsync(ExploreDbContext context, Guid tenantId, Guid eventId, CancellationToken token = default) =>
        new EfCoreUnitOfWork(context).ExecuteBootstrapConvergenceAsync(
            ct => new AnonymousRegistrationChallengeQuota(context).TryAcquireAsync(tenantId, eventId, ct), token);

    private static async Task LimitsAsync(ExploreDbContext context, string tenantLimit, string eventLimit, bool locked = false)
    {
        context.SystemSettings.AddRange(
            new SystemSetting { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.AnonymousRegistrationChallenge.TenantPermitsPerMinute,
                Value = $"\"{tenantLimit}\"", ValueType = SettingValueType.String, IsLocked = locked },
            new SystemSetting { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.AnonymousRegistrationChallenge.EventPermitsPerMinute,
                Value = $"\"{eventLimit}\"", ValueType = SettingValueType.String, IsLocked = locked });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static ExploreDbContext Context(EventVisitorCapabilitySqliteFixture fixture, Guid tenantId, params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnectionStringBuilder { DataSource = fixture.DatabasePath, Pooling = false, DefaultTimeout = 15 };
        return new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>().UseSqlite(connection.ToString())
            .UseSnakeCaseNamingConvention().AddInterceptors(interceptors.OfType<DatabaseClock>().Any()
                ? interceptors : [.. interceptors, new DatabaseClock()]).Options)
        { TenantContext = new TenantScope(tenantId) };
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private sealed class DatabaseClock : DbCommandInterceptor
    {
        public DateTime UtcNow { get; set; } = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT CURRENT_TIMESTAMP AS \"Value\"", StringComparison.Ordinal))
            {
                Reads++;
                command.CommandText = command.CommandText.Replace("CURRENT_TIMESTAMP", "@quota_clock", StringComparison.Ordinal);
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@quota_clock";
                parameter.Value = UtcNow;
                command.Parameters.Add(parameter);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransactionBarrier(bool holdAfterStart) : DbTransactionInterceptor
    {
        public TaskCompletionSource Starting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Starting.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (holdAfterStart) await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
