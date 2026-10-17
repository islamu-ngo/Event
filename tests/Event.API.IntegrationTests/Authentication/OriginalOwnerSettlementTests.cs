using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class OriginalOwnerSettlementTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalOwnerPublishesOnlyItsAcknowledgedPhysicalOutcome(bool commit)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        object invocation = new();
        string key = $"physical-outcome-{Guid.NewGuid():N}";
        int attempts = 0;
        var settlement = await owner.ExecuteAsync(invocation, async token =>
        {
            attempts++;
            await AcquireOriginalSourcesAsync(fixture, token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            return commit ? OriginalOwnerDecision<string>.Commit(key) : OriginalOwnerDecision<string>.RollBack(key);
        });

        var disposition = commit ? OriginalOwnerDisposition.Committed : OriginalOwnerDisposition.WholeTransactionRolledBack;
        await Assert.That(settlement.Disposition).IsEqualTo(disposition);
        await Assert.That(owner.Consume(invocation, settlement, disposition)).IsEqualTo(key);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.ChangeTracker.HasChanges()).IsFalse();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key))
            .IsEqualTo(commit);
        Assert.Throws<InvalidOperationException>(() => owner.Consume(invocation, settlement, disposition));
    }

    [Test]
    public async Task ReceiptsCannotMoveAcrossInvocationsOwnersOrDisposition()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        var other = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        object invocation = new();
        var settlement = await owner.ExecuteAsync(invocation,
            _ => Task.FromResult(OriginalOwnerDecision<string>.RollBack("observation")));

        Assert.Throws<InvalidOperationException>(() => owner.Consume(new object(), settlement,
            OriginalOwnerDisposition.WholeTransactionRolledBack));
        Assert.Throws<InvalidOperationException>(() => other.Consume(invocation, settlement,
            OriginalOwnerDisposition.WholeTransactionRolledBack));
        Assert.Throws<InvalidOperationException>(() => owner.Consume(invocation, settlement,
            OriginalOwnerDisposition.Committed));
        Assert.Throws<InvalidOperationException>(() => owner.Consume(invocation, new ForgedSettlement(),
            OriginalOwnerDisposition.WholeTransactionRolledBack));
        await Assert.That(owner.Consume(invocation, settlement, OriginalOwnerDisposition.WholeTransactionRolledBack))
            .IsEqualTo("observation");
    }

    [Test]
    public async Task StartingAnotherAttemptInvalidatesUnconsumedSettlement()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        object invocation = new();
        var first = await owner.ExecuteAsync(invocation,
            _ => Task.FromResult(OriginalOwnerDecision<int>.RollBack(1)));
        var second = await owner.ExecuteAsync(invocation,
            _ => Task.FromResult(OriginalOwnerDecision<int>.RollBack(2)));
        Assert.Throws<InvalidOperationException>(() => owner.Consume(invocation, first,
            OriginalOwnerDisposition.WholeTransactionRolledBack));
        await Assert.That(owner.Consume(invocation, second, OriginalOwnerDisposition.WholeTransactionRolledBack))
            .IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CallbackExceptionsCannotSupplyDirectNestedOrAggregateOwnerReceipts(int nesting)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        string key = $"forged-outcome-{Guid.NewGuid():N}";
        int attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync<string>(new object(), async token =>
        {
            attempts++;
            await AcquireOriginalSourcesAsync(fixture, token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            Exception forged = new HumanMutationAttemptRolledBackException(new SqliteException("Callback receipt", 5));
            throw nesting switch
            {
                1 => new InvalidOperationException("Nested", forged),
                2 => new AggregateException(new InvalidOperationException("Nested", forged)),
                _ => forged
            };
        }));
        await Assert.That(attempts).IsEqualTo(1);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostAcknowledgementNeverReturnsSettlementEvenWhenProviderAlreadySettled(bool commit)
    {
        var interceptor = new LostAcknowledgement(commit);
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(interceptor);
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        string key = $"unknown-outcome-{Guid.NewGuid():N}";
        interceptor.Armed = true;
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => owner.ExecuteAsync(new object(), async token =>
        {
            await AcquireOriginalSourcesAsync(fixture, token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            return commit ? OriginalOwnerDecision<string>.Commit(key) : OriginalOwnerDecision<string>.RollBack(key);
        }));
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.ChangeTracker.HasChanges()).IsFalse();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key))
            .IsEqualTo(commit);
    }

    [Test]
    public async Task BorrowedTransactionCannotPublishSettlementOrExecuteCallback()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        int attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(new object(), _ =>
        {
            attempts++;
            return Task.FromResult(OriginalOwnerDecision<int>.RollBack(1));
        }));
        await Assert.That(attempts).IsEqualTo(0);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ActualFinalCredentialExpiryRollsBackWithoutReturningAnOutcome()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        string key = $"expired-outcome-{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(new object(), async token =>
        {
            await AcquireOriginalSourcesAsync(fixture, token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            fixture.Clock.Advance(TimeSpan.FromDays(365));
            return OriginalOwnerDecision<string>.Commit(key);
        }));
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SharedSqliteOuterOwnerSettlesBothContextsBeforePublishing(bool commit)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var factory = fixture.Services.GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
        var authority = new EmbeddedPrivacyErasureAuthorityRepository(factory, fixture.Clock,
            fixture.Services.GetRequiredService<IOptions<PrivacyErasureOptions>>(), applicationContext: fixture.Context);
        var owner = (IOriginalOwnerSettlementBoundary)authority;
        var borrowed = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        object invocation = new();
        string key = $"shared-physical-{Guid.NewGuid():N}";
        long before = (await authority.GetStateAsync()).HighWaterSequence;
        var settlement = await owner.ExecuteAsync(invocation, async token =>
        {
            await AcquireOriginalSourcesAsync(fixture, token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => borrowed.ExecuteAsync(invocation,
                _ => Task.FromResult(OriginalOwnerDecision<string>.RollBack("borrowed"))));
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(invocation,
                _ => Task.FromResult(OriginalOwnerDecision<string>.RollBack("nested"))));
            await authority.AppendAsync(new PrivacyErasureRequest(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1), token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            return commit ? OriginalOwnerDecision<string>.Commit(key) : OriginalOwnerDecision<string>.RollBack(key);
        });
        var disposition = commit ? OriginalOwnerDisposition.Committed : OriginalOwnerDisposition.WholeTransactionRolledBack;
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        Assert.Throws<InvalidOperationException>(() => borrowed.Consume(invocation, settlement, disposition));
        await Assert.That(owner.Consume(invocation, settlement, disposition)).IsEqualTo(key);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key))
            .IsEqualTo(commit);
        await Assert.That((await authority.GetStateAsync()).HighWaterSequence).IsEqualTo(before + (commit ? 1 : 0));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PhysicallyEndedCallbackCannotTurnItsProposedRollbackIntoProof(bool commit)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        string key = $"ended-physical-{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => owner.ExecuteAsync(new object(), async token =>
        {
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            if (commit) await fixture.Context.Database.CurrentTransaction!.CommitAsync(token);
            else await fixture.Context.Database.CurrentTransaction!.RollbackAsync(token);
            return OriginalOwnerDecision<string>.RollBack(key);
        }));
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key))
            .IsEqualTo(commit);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ArbitraryAndProviderFailuresRollBackWithoutAnySettlementResult(bool providerFailure)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        string key = $"exception-physical-{Guid.NewGuid():N}";
        int attempts = 0;
        Exception failure = providerFailure ? new SqliteException("Callback provider failure", 5)
            : new InvalidOperationException("Callback storage failure");
        try
        {
            await owner.ExecuteAsync<string>(new object(), async token =>
            {
                attempts++;
                await AcquireOriginalSourcesAsync(fixture, token);
                fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
                await fixture.Context.SaveChangesAsync(token);
                throw failure;
            });
            throw new InvalidOperationException("A failed callback returned settlement.");
        }
        catch (Exception observed) when (ReferenceEquals(observed, failure))
        {
        }
        await Assert.That(attempts).IsEqualTo(1);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SharedSqliteLostAcknowledgementWithholdsBothContextsSettlement(bool commit)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var interceptor = new LostAcknowledgement(commit);
        var originalFactory = fixture.Services.GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
        var factory = new InterceptedAuthorityFactory(originalFactory, interceptor);
        var authority = new EmbeddedPrivacyErasureAuthorityRepository(factory, fixture.Clock,
            fixture.Services.GetRequiredService<IOptions<PrivacyErasureOptions>>(), applicationContext: fixture.Context);
        var owner = (IOriginalOwnerSettlementBoundary)authority;
        string key = $"shared-unknown-{Guid.NewGuid():N}";
        long before = (await authority.GetStateAsync()).HighWaterSequence;
        interceptor.Armed = true;
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => owner.ExecuteAsync(new object(), async token =>
        {
            await AcquireOriginalSourcesAsync(fixture, token);
            await authority.AppendAsync(new PrivacyErasureRequest(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1), token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            return commit ? OriginalOwnerDecision<string>.Commit(key) : OriginalOwnerDecision<string>.RollBack(key);
        }));
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.ChangeTracker.HasChanges()).IsFalse();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key))
            .IsEqualTo(commit);
        await Assert.That((await authority.GetStateAsync()).HighWaterSequence).IsEqualTo(before + (commit ? 1 : 0));
    }

    [Test]
    public async Task RejectionRollbackDoesNotRetainFinalizersInTheNextOwner()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        object invocation = new();
        var rolledBack = await owner.ExecuteAsync(invocation, async token =>
        {
            await AcquireOriginalSourcesAsync(fixture, token);
            fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ => Task.CompletedTask,
                _ => throw new UnauthorizedAccessException("Old rollback-only finalizer must not survive."));
            return OriginalOwnerDecision<int>.RollBack(1);
        });
        owner.Consume(invocation, rolledBack, OriginalOwnerDisposition.WholeTransactionRolledBack);
        string key = $"fresh-settlement-{Guid.NewGuid():N}";
        object nextInvocation = new();
        var committed = await owner.ExecuteAsync(nextInvocation, async token =>
        {
            await AcquireOriginalSourcesAsync(fixture, token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "fresh" });
            await fixture.Context.SaveChangesAsync(token);
            return OriginalOwnerDecision<string>.Commit(key);
        });
        await Assert.That(owner.Consume(nextInvocation, committed, OriginalOwnerDisposition.Committed)).IsEqualTo(key);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key)).IsTrue();
    }

    [Test]
    public async Task ReplacingTheContextConnectionInvalidatesItsPhysicalReceipt()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        object invocation = new();
        var settlement = await owner.ExecuteAsync(invocation,
            _ => Task.FromResult(OriginalOwnerDecision<int>.RollBack(1)));
        var original = fixture.Context.Database.GetDbConnection();
        await fixture.Context.Database.CloseConnectionAsync();
        await using var replacement = new SqliteConnection(original.ConnectionString);
        fixture.Context.Database.SetDbConnection(replacement, contextOwnsConnection: false);
        try
        {
            Assert.Throws<InvalidOperationException>(() => owner.Consume(invocation, settlement,
                OriginalOwnerDisposition.WholeTransactionRolledBack));
        }
        finally
        {
            fixture.Context.Database.SetDbConnection(original, contextOwnsConnection: false);
        }
    }

    [Test]
    public async Task CancellationAfterPersistedWorkRollsBackButIssuesNoReceipt()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        using var cancellation = new CancellationTokenSource();
        string key = $"cancelled-settlement-{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<OperationCanceledException>(() => owner.ExecuteAsync(new object(), async token =>
        {
            await AcquireOriginalSourcesAsync(fixture, token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            cancellation.Cancel();
            return OriginalOwnerDecision<string>.RollBack(key);
        }, cancellation.Token));
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    private static async Task AcquireOriginalSourcesAsync(ResourceVisitorAuthenticatedFixture fixture, CancellationToken token)
    {
        var exclusion = await fixture.Services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(token);
        var principal = await fixture.Services.GetRequiredService<IHumanPrincipalSourceFence>().AcquireAsync(exclusion, token);
        var route = await fixture.Services.GetRequiredService<IHumanAuthorizationRouteSource>().AcquireAsync(exclusion, token);
        fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
        {
            await principal.ValidateAsync(finalToken);
            await route.ValidateAsync(finalToken);
        }, now =>
        {
            if (!fixture.Lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The actual authenticated lifetime ended.");
        });
    }

    private sealed class ForgedSettlement : IOriginalOwnerSettlement<string>
    {
        public OriginalOwnerDisposition Disposition => OriginalOwnerDisposition.WholeTransactionRolledBack;
    }

    private sealed class InterceptedAuthorityFactory(
        IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext> original, DbTransactionInterceptor interceptor)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext()
        {
            using var template = original.CreateDbContext();
            var options = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>(
                (DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext>)template.GetService<IDbContextOptions>())
                .AddInterceptors(interceptor).Options;
            return new(options);
        }
    }

    private sealed class LostAcknowledgement(bool commit) : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || !commit) return Task.CompletedTask;
            Armed = false;
            throw new SqliteException("Lost physical commit acknowledgement.", 5);
        }

        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || commit) return Task.CompletedTask;
            Armed = false;
            throw new InvalidOperationException("Lost physical rollback acknowledgement.");
        }
    }
}
