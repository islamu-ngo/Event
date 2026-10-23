using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class ConfiguredManagedTenantLocalEligibilitySourceTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ExactNonsecretImageRemainsCurrentAcrossOriginalGlobalSettlementWithoutOwningThatContext(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var contextId = application.ContextId;
        await application.Database.OpenConnectionAsync(fixture.CancellationToken);
        await using var source = new ConfiguredManagedTenantLocalEligibilitySource(application, topology,
            CreateFactory(fixture, scope), fixture.Clock);
        var lease = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        try
        {
            await Assert.That(lease.Eligibility.LocalSubjectId).IsEqualTo(fixture.Receipt.LocalSubjectId);
            await Assert.That(lease.Eligibility.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
            await Assert.That(lease.Eligibility.PersonalActorId).IsEqualTo(fixture.Receipt.PersonalActorId);
            await Assert.That(lease.Eligibility.ExternalLoginId).IsEqualTo(fixture.Receipt.ExternalLoginId);
            await Assert.That(lease.Eligibility.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
            await Assert.That(lease.Eligibility.Generation > 0).IsTrue();
            await Assert.That(application.Database.CurrentTransaction).IsNull();
            await using (var transaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken))
            {
                await lease.RequireCurrentAsync(fixture.CancellationToken);
                await transaction.CommitAsync(fixture.CancellationToken);
            }
            await lease.RequireCurrentAsync(fixture.CancellationToken);
            await Assert.That(async () =>
            {
                await using var unexpected = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
            }).Throws<InvalidOperationException>();
        }
        finally { await lease.DisposeAsync(); }

        await source.DisposeAsync();
        await using var caller = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
        await lease.DisposeAsync();
        await source.DisposeAsync();
        await Assert.That(application.ContextId).IsEqualTo(contextId);
        application.RequireBoundTenantLease();
        await Assert.That(application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        await Assert.That(application.Database.CurrentTransaction).IsSameReferenceAs(caller);
        await Assert.That(async () =>
        {
            await using var unexpected = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        }).Throws<ObjectDisposedException>();
        await caller.RollbackAsync(fixture.CancellationToken);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task BlockedSelectionCancelsOrAbandonsWithoutChangingThePointerOrClosingTheOriginalGlobal(
        IdentityDatabaseTopology topology, bool abandon)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var holderScope = fixture.Provider.CreateAsyncScope();
        await using var holder = await LocalIdentityLifecycleGateCustody.EnterAsync(fixture.Identity(holderScope), fixture.CancellationToken);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await application.Database.OpenConnectionAsync(fixture.CancellationToken);
        var factory = CreateFactory(fixture, scope);
        await using var source = new ConfiguredManagedTenantLocalEligibilitySource(application, topology, factory, fixture.Clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.CancellationToken);
        Task<IManagedTenantLocalEligibilityLease>? pending = null;
        try
        {
            pending = source.AcquireAsync(fixture.Receipt.LocalSubjectId, cancellation.Token);
            await Assert.That(pending.IsCompleted).IsFalse();
            if (abandon)
            {
                await source.DisposeAsync();
                await Assert.That(cancellation.IsCancellationRequested).IsFalse();
            }
            else cancellation.Cancel();
            await Assert.That(async () => { await pending; }).Throws<OperationCanceledException>();
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                if (pending is not null)
                {
                    await using var unexpected = await pending;
                }
            }
            catch (Exception) { /* Observe failed acquisition without masking the original invariant failure. */ }
            finally { await holder.DisposeAsync(); }
        }

        await Assert.That(application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        application.RequireBoundTenantLease();
        await using var replacement = new ConfiguredManagedTenantLocalEligibilitySource(application, topology, factory, fixture.Clock);
        await using var retry = await (abandon ? replacement : source)
            .AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(retry.Eligibility.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
        await Assert.That(retry.Eligibility.ExternalLoginId).IsEqualTo(fixture.Receipt.ExternalLoginId);
        await Assert.That(retry.Eligibility.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
        await retry.RequireCurrentAsync(fixture.CancellationToken);
    }

    [Test]
    public async Task ReleasedExternalReaderCannotCloseTheNextPooledIdentityOwnerOrTheOriginalGlobal()
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await application.Database.OpenConnectionAsync(fixture.CancellationToken);
        var factory = CreateFactory(fixture, scope)!;
        var originalIdentity = await factory.CreateExternalIdentityAsync(fixture.CancellationToken);
        await originalIdentity.DisposeAsync();
        await using var source = new ConfiguredManagedTenantLocalEligibilitySource(application,
            IdentityDatabaseTopology.External, factory, fixture.Clock);
        var lease = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await lease.RequireCurrentAsync(fixture.CancellationToken);
        await lease.DisposeAsync();
        await source.DisposeAsync();

        await using var next = await factory.CreateExternalIdentityAsync(fixture.CancellationToken);
        await Assert.That(next).IsSameReferenceAs(originalIdentity);
        await using var identityTransaction = await next.Database.BeginTransactionAsync(fixture.CancellationToken);
        await using var applicationTransaction = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
        await lease.DisposeAsync();
        await source.DisposeAsync();
        await Assert.That(next.Database.CurrentTransaction).IsSameReferenceAs(identityTransaction);
        await Assert.That(application.Database.CurrentTransaction).IsSameReferenceAs(applicationTransaction);
        await Assert.That(next.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        await Assert.That(application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        var row = await next.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(value => value.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(row.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
        await Assert.That(async () => await lease.RequireCurrentAsync(fixture.CancellationToken)).Throws<UnauthorizedAccessException>();
        await identityTransaction.RollbackAsync(fixture.CancellationToken);
        await applicationTransaction.RollbackAsync(fixture.CancellationToken);
    }

    private static LocalIdentityWriterDbContextFactory? CreateFactory(LocalCredentialFirstUseTests.Fixture fixture,
        AsyncServiceScope scope)
    {
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        DbContext identity = fixture.Identity(scope);
        if (ReferenceEquals(identity, application)) return null;
        return new(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = new SqliteConnectionStringBuilder(application.Database.GetConnectionString()).DataSource,
        }, new ExternalIdentityDatabaseDescriptor(PrimaryDatabaseProvider.Sqlite,
            identity.Database.GetConnectionString()!, IdentityDatabaseConfiguration.DefaultSchema, null, null), poolSize: 1);
    }
}
