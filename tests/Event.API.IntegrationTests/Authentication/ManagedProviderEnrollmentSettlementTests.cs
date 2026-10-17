using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
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
public sealed class ManagedProviderEnrollmentSettlementTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task EnrollmentPublishesOnlyAfterOriginalOwnerDisposalAndKeepsPostcommitOrder(
        bool externalTarget, bool commit)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var acquireSources = PrepareSources(fixture);
        var authority = Authority(fixture);
        var operation = new PrivacyIdentityFenceOperation(authority);
        IOriginalOwnerSettlementBoundary owner = externalTarget
            ? authority : new EfCoreUnitOfWork(fixture.Context);
        var account = Account(fixture, externalTarget);
        var callbacks = new List<int>();
        string key = "enrollment-" + Guid.CreateVersion7().ToString("N");
        int attempts = 0;
        long before = (await authority.GetStateAsync()).HighWaterSequence;
        string result = await operation.ExecuteSettledEnrollmentAsync(account, new object(), owner, async token =>
        {
            attempts++;
            await acquireSources(token);
            if (externalTarget)
            {
                await operation.EnsureSubjectMayEnrollAsync(fixture.UserId, token);
                await authority.AppendAsync(new PrivacyErasureRequest(Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User, Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1), token);
            }
            await operation.AfterEnrollmentCommitAsync(() =>
            {
                AssertDisposed();
                callbacks.Add(1);
                return Task.CompletedTask;
            });
            // Legacy nested enrollment borrows the already active application scope;
            // it must neither open another embedded transaction nor publish early.
            await operation.ExecuteEnrollmentAsync(account, async nestedToken =>
            {
                nestedToken.ThrowIfCancellationRequested();
                await operation.AfterEnrollmentCommitAsync(() =>
                {
                    AssertDisposed();
                    callbacks.Add(2);
                    return Task.CompletedTask;
                });
                return true;
            }, token);
            fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await fixture.Context.SaveChangesAsync(token);
            await Assert.That(callbacks.Count).IsEqualTo(0);
            return commit ? OriginalOwnerDecision<string>.Commit(key) : OriginalOwnerDecision<string>.RollBack(key);
        }, default);
        await Assert.That(result).IsEqualTo(key);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(string.Join(",", callbacks)).IsEqualTo(commit ? "1,2" : "");
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.EnsureSubjectMayEnrollAsync(fixture.UserId, default));
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsEqualTo(commit);
        await Assert.That((await authority.GetStateAsync()).HighWaterSequence)
            .IsEqualTo(before + (externalTarget && commit ? 1 : 0));

        void AssertDisposed()
        {
            if (fixture.Context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("An enrollment callback escaped before physical disposal.");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TerminalCredentialDenialRollsBackActualEnrollmentAndSuppressesPostcommit(bool externalTarget)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var acquireSources = PrepareSources(fixture);
        var authority = Authority(fixture);
        var operation = new PrivacyIdentityFenceOperation(authority);
        IOriginalOwnerSettlementBoundary owner = externalTarget
            ? authority : new EfCoreUnitOfWork(fixture.Context);
        string key = "denied-enrollment-" + Guid.CreateVersion7().ToString("N");
        bool published = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            operation.ExecuteSettledEnrollmentAsync(Account(fixture, externalTarget), new object(), owner, async token =>
            {
                await acquireSources(token);
                fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
                await fixture.Context.SaveChangesAsync(token);
                await operation.AfterEnrollmentCommitAsync(() =>
                {
                    published = true;
                    return Task.CompletedTask;
                });
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await fixture.Context.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key, finalToken);
                    fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                }, _ => { });
                return OriginalOwnerDecision<string>.Commit(key);
            }, default));
        await Assert.That(published).IsFalse();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmbeddedLostSettlementAcknowledgementNeverPublishesOrReplays(bool commit)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var acquireSources = PrepareSources(fixture);
        var interceptor = new LostAcknowledgement(commit);
        var factory = new InterceptedAuthorityFactory(fixture.Services
            .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>(), interceptor);
        var authority = Authority(fixture, factory);
        var operation = new PrivacyIdentityFenceOperation(authority);
        _ = await authority.GetStateAsync();
        interceptor.Armed = true;
        int attempts = 0;
        bool published = false;
        string key = "unknown-enrollment-" + Guid.CreateVersion7().ToString("N");
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() =>
            operation.ExecuteSettledEnrollmentAsync(Account(fixture, true), new object(), authority, async token =>
            {
                attempts++;
                await acquireSources(token);
                fixture.Context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
                await fixture.Context.SaveChangesAsync(token);
                await operation.AfterEnrollmentCommitAsync(() =>
                {
                    published = true;
                    return Task.CompletedTask;
                });
                return commit ? OriginalOwnerDecision<string>.Commit(key) : OriginalOwnerDecision<string>.RollBack(key);
            }, default));
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(published).IsFalse();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsEqualTo(commit);
    }

    [Test]
    public async Task SettledHumanEnrollmentCannotBorrowLegacyActiveGate()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var authority = Authority(fixture);
        var operation = new PrivacyIdentityFenceOperation(authority);
        var account = Account(fixture, true);
        int attempts = 0;
        await operation.ExecuteEnrollmentAsync(account, async token =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                operation.ExecuteSettledEnrollmentAsync(account, new object(), authority, _ =>
                {
                    attempts++;
                    return Task.FromResult(OriginalOwnerDecision<int>.Commit(1));
                }, token));
            return true;
        }, default);
        await Assert.That(attempts).IsEqualTo(0);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task DifferentOperationCannotBorrowAnotherInvocationsPhysicalIdentityGate()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var authority = Authority(fixture);
        var operation = new PrivacyIdentityFenceOperation(authority);
        var owner = (IOriginalOwnerSettlementBoundary)new EfCoreUnitOfWork(fixture.Context);
        int attempts = 0;
        await authority.ExecuteSerializedAsync(async token =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                operation.ExecuteSettledEnrollmentAsync(Account(fixture, true), new object(), owner, _ =>
                {
                    attempts++;
                    return Task.FromResult(OriginalOwnerDecision<int>.Commit(1));
                }, token));
            return true;
        }, default);
        await Assert.That(attempts).IsEqualTo(0);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    private static ProviderAccountKey Account(ResourceVisitorAuthenticatedFixture fixture, bool external) =>
        external ? PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://enrollment.example.test", "target")
            : new ProviderAccountKey(AuthenticationProviderKind.Local, fixture.UserId.ToString("D"));

    private static EmbeddedPrivacyErasureAuthorityRepository Authority(ResourceVisitorAuthenticatedFixture fixture,
        IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>? factory = null) =>
        new(factory ?? fixture.Services.GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>(),
            fixture.Clock, fixture.Services.GetRequiredService<IOptions<PrivacyErasureOptions>>(),
            applicationContext: fixture.Context);

    private static Func<CancellationToken, Task> PrepareSources(ResourceVisitorAuthenticatedFixture fixture)
    {
        var exclusionSource = fixture.Services.GetRequiredService<IPolicySourceSetFence>();
        var principalSource = fixture.Services.GetRequiredService<IHumanPrincipalSourceFence>();
        var routeSource = fixture.Services.GetRequiredService<IHumanAuthorizationRouteSource>();
        var finalizer = fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>();
        return async token =>
        {
            var exclusion = await exclusionSource.AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(exclusion, token);
            var route = await routeSource.AcquireAsync(exclusion, token);
            finalizer.Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                await principal.ValidateAsync(finalToken);
                await route.ValidateAsync(finalToken);
            }, now =>
            {
                if (!fixture.Lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("The original authenticated enrollment lifetime ended.");
            });
        };
    }

    private sealed class InterceptedAuthorityFactory(
        IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext> original, DbTransactionInterceptor interceptor)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext()
        {
            using var template = original.CreateDbContext();
            return new(new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>(
                (DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext>)template.GetService<IDbContextOptions>())
                .AddInterceptors(interceptor).Options);
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
            throw new SqliteException("Lost enrollment commit acknowledgement.", 5);
        }

        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || commit) return Task.CompletedTask;
            Armed = false;
            throw new InvalidOperationException("Lost enrollment rollback acknowledgement.");
        }
    }
}
