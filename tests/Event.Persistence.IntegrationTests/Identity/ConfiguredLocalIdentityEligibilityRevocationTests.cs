using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class ConfiguredLocalIdentityEligibilityRevocationTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task OriginalApplicationRollbackPreservesTheSelectedTopologyGuaranteeAndReplayIsTerminal(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var authority = await RetainedAuthority.CreateAsync(fixture.Clock);
        PrivacyErasureIntent intent = await authority.AppendAsync(fixture.Receipt.LocalSubjectId);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await using var service = Create(fixture, scope, authority.Source);
        long before = (await ReadAsync(fixture)).Generation;
        await using (var lease = await service.AcquireAsync(intent, fixture.CancellationToken))
        {
            await using var transaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
            await service.InvalidateInCurrentTransactionAsync(intent, fixture.CancellationToken);
            if (topology == IdentityDatabaseTopology.Colocated)
            {
                var pending = await application.Set<LocalIdentityEligibility>().AsNoTracking()
                    .SingleAsync(row => row.LocalSubjectId == intent.SubjectId, fixture.CancellationToken);
                await Assert.That(pending.State).IsEqualTo(LocalIdentityEligibilityState.Deleted);
            }
            await transaction.RollbackAsync(fixture.CancellationToken);
        }

        LocalIdentityEligibility afterRollback = await ReadAsync(fixture);
        await Assert.That(afterRollback.State).IsEqualTo(topology == IdentityDatabaseTopology.Colocated
            ? LocalIdentityEligibilityState.Ready : LocalIdentityEligibilityState.Deleted);
        await Assert.That(afterRollback.Generation).IsEqualTo(topology == IdentityDatabaseTopology.Colocated ? before : before + 1);
        await using (var replay = await service.AcquireAsync(intent, fixture.CancellationToken))
        {
            await using var transaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
            await service.InvalidateInCurrentTransactionAsync(intent, fixture.CancellationToken);
            await transaction.CommitAsync(fixture.CancellationToken);
        }
        LocalIdentityEligibility final = await ReadAsync(fixture);
        await Assert.That(final.State).IsEqualTo(LocalIdentityEligibilityState.Deleted);
        await Assert.That(final.Generation).IsEqualTo(before + 1);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReentryCannotReplaceTheRetainedIntentAndRepeatedCleanupDoesNotTouchTheCallerTransaction(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var authority = await RetainedAuthority.CreateAsync(fixture.Clock);
        var intent = await authority.AppendAsync(fixture.Receipt.LocalSubjectId);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await using var service = Create(fixture, scope, authority.Source);
        var lease = await service.AcquireAsync(intent, fixture.CancellationToken);
        try
        {
            await Assert.That(async () =>
            {
                await using var unexpected = await service.AcquireAsync(intent, fixture.CancellationToken);
            }).Throws<InvalidOperationException>();
            await using var transaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
            await service.InvalidateInCurrentTransactionAsync(intent, fixture.CancellationToken);
            await transaction.CommitAsync(fixture.CancellationToken);
        }
        finally { await lease.DisposeAsync(); }

        await application.Database.OpenConnectionAsync(fixture.CancellationToken);
        await using var callerTransaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
        await lease.DisposeAsync();
        await service.DisposeAsync();
        await Assert.That(application.Database.CurrentTransaction).IsSameReferenceAs(callerTransaction);
        await Assert.That(application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        await callerTransaction.RollbackAsync(fixture.CancellationToken);
        await Assert.That((await ReadAsync(fixture)).State).IsEqualTo(LocalIdentityEligibilityState.Deleted);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CancellationWhileTheRealLifecycleResourceIsHeldReleasesAcquisitionForRetry(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var authority = await RetainedAuthority.CreateAsync(fixture.Clock);
        var intent = await authority.AppendAsync(fixture.Receipt.LocalSubjectId);
        await using var holderScope = fixture.Provider.CreateAsyncScope();
        await using var holder = await LocalIdentityLifecycleGateCustody.EnterAsync(fixture.Identity(holderScope), fixture.CancellationToken);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await using var service = Create(fixture, scope, authority.Source);
        LocalIdentityEligibility before = await ReadAsync(fixture);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<IAsyncDisposable> pending = service.AcquireAsync(intent, cancellation.Token);
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            cancellation.Cancel();
            await Assert.That(async () => await pending).Throws<OperationCanceledException>();
        }
        finally
        {
            cancellation.Cancel();
            try { if (await pending is { } acquired) await acquired.DisposeAsync(); }
            catch (OperationCanceledException) { }
            finally { await holder.DisposeAsync(); }
        }

        LocalIdentityEligibility afterCancellation = await ReadAsync(fixture);
        await Assert.That(afterCancellation.State).IsEqualTo(before.State);
        await Assert.That(afterCancellation.Generation).IsEqualTo(before.Generation);
        await using var retry = await service.AcquireAsync(intent, fixture.CancellationToken);
        await using var transaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
        await service.InvalidateInCurrentTransactionAsync(intent, fixture.CancellationToken);
        await transaction.CommitAsync(fixture.CancellationToken);
        await Assert.That((await ReadAsync(fixture)).State).IsEqualTo(LocalIdentityEligibilityState.Deleted);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task OwnerAbandonmentCancelsItsBlockedAcquisitionWithoutClosingTheOriginalGlobalConnector(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var authority = await RetainedAuthority.CreateAsync(fixture.Clock);
        var intent = await authority.AppendAsync(fixture.Receipt.LocalSubjectId);
        await using var holderScope = fixture.Provider.CreateAsyncScope();
        await using var holder = await LocalIdentityLifecycleGateCustody.EnterAsync(fixture.Identity(holderScope), fixture.CancellationToken);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await application.Database.OpenConnectionAsync(fixture.CancellationToken);
        await using var service = Create(fixture, scope, authority.Source);
        LocalIdentityEligibility before = await ReadAsync(fixture);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<IAsyncDisposable> pending = service.AcquireAsync(intent, cancellation.Token);
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await service.DisposeAsync();
            await Assert.That(cancellation.IsCancellationRequested).IsFalse();
            await Assert.That(async () => await pending).Throws<OperationCanceledException>();
            await Assert.That(application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
            await Assert.That(async () => await service.AcquireAsync(intent, fixture.CancellationToken))
                .Throws<ObjectDisposedException>();
        }
        finally
        {
            cancellation.Cancel();
            try { if (await pending is { } acquired) await acquired.DisposeAsync(); }
            catch (OperationCanceledException) { }
            finally { await holder.DisposeAsync(); }
        }
        LocalIdentityEligibility afterAbandonment = await ReadAsync(fixture);
        await Assert.That(afterAbandonment.State).IsEqualTo(before.State);
        await Assert.That(afterAbandonment.Generation).IsEqualTo(before.Generation);
        await using var replacement = Create(fixture, scope, authority.Source);
        await using var lease = await replacement.AcquireAsync(intent, fixture.CancellationToken);
        await using var transaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
        await replacement.InvalidateInCurrentTransactionAsync(intent, fixture.CancellationToken);
        await transaction.CommitAsync(fixture.CancellationToken);
    }

    private static ConfiguredLocalIdentityEligibilityRevocation Create(LocalCredentialFirstUseTests.Fixture fixture,
        AsyncServiceScope scope, IPrivacyErasureAuthority authority)
    {
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        DbContext identity = fixture.Identity(scope);
        if (ReferenceEquals(identity, application))
            return new(application, IdentityDatabaseTopology.Colocated, null, authority, fixture.Clock);

        var options = new PrimaryDatabaseConnectionOptions
        {
            Provider = PrimaryDatabaseProvider.Sqlite,
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Database = new SqliteConnectionStringBuilder(application.Database.GetConnectionString()).DataSource,
        };
        var external = new ExternalIdentityDatabaseDescriptor(
            PrimaryDatabaseProvider.Sqlite, identity.Database.GetConnectionString()!, IdentityDatabaseConfiguration.DefaultSchema, null, null);
        return new(application, IdentityDatabaseTopology.External,
            new LocalIdentityWriterDbContextFactory(options, external), authority, fixture.Clock);
    }

    private static async Task<LocalIdentityEligibility> ReadAsync(LocalCredentialFirstUseTests.Fixture fixture)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        return await fixture.Identity(scope).Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
    }

    private sealed class RetainedAuthority : IAsyncDisposable, IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        private readonly SqliteConnection connection = new(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString());
        private DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options = null!;
        internal IPrivacyErasureAuthority Source { get; private set; } = null!;

        internal static async Task<RetainedAuthority> CreateAsync(TimeProvider clock)
        {
            var owner = new RetainedAuthority();
            try
            {
                await owner.connection.OpenAsync();
                owner.options = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>()
                    .UseSqlite(owner.connection.ConnectionString).UseSnakeCaseNamingConvention().Options;
                await using var context = owner.CreateDbContext();
                await context.Database.EnsureCreatedAsync();
                owner.Source = new EmbeddedPrivacyErasureAuthorityRepository(owner, clock, Options.Create(new PrivacyErasureOptions()));
                return owner;
            }
            catch { await owner.DisposeAsync(); throw; }
        }

        internal Task<PrivacyErasureIntent> AppendAsync(Guid subject) => Source.AppendAsync(new(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, subject, PrivacyErasureReasonCode.AccountDeletion, 1));

        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
