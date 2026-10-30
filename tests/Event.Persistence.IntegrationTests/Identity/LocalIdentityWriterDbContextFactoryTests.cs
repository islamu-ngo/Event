using System.Data;
using Explore.Domain;
using Explore.Persistence.Identity;
using Explore.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalIdentityWriterDbContextFactoryTests
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    public async Task OrdinaryCredentialsCannotConstructThePrivateWriterFactory(PrimaryDatabaseRole role)
    {
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = $"unused_writer_{Guid.CreateVersion7():N}.sqlite",
        };
        await Assert.That(() => new LocalIdentityWriterDbContextFactory(options)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task CreatedPairUsesTheImmutableConfiguredStoresAndAnAlreadySealedExactTenant(
        IdentityDatabaseTopology topology, bool tenantSelected)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var factory = Create(fixture, scope);
        Guid? tenant = tenantSelected ? Guid.CreateVersion7() : null;
        await using var pair = await factory.CreateAsync(tenant, fixture.CancellationToken);
        pair.Application.RequireBoundTenantLease();
        await Assert.That(pair.Application.TenantFilterTenantId).IsEqualTo(tenant);
        await Assert.That(pair.Application.IsTenantFilterBypassed).IsFalse();
        await Assert.That(pair.Application.Database.CurrentTransaction).IsNull();
        await Assert.That(pair.Identity.Database.CurrentTransaction).IsNull();
        await Assert.That(pair.Application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(ReferenceEquals(pair.Application, pair.Identity)).IsEqualTo(topology == IdentityDatabaseTopology.Colocated);
        if (topology == IdentityDatabaseTopology.External)
            await Assert.That(pair.Identity is ExternalIdentityDbContext).IsTrue();
        var published = await pair.Identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(published.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
        await Assert.That(published.ExternalLoginId).IsEqualTo(fixture.Receipt.ExternalLoginId);
    }

    [Test]
    public async Task ExternalOnlyCreationDoesNotOpenAnApplicationTargetOrTouchItsOriginalCallerTransaction()
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<Explore.Persistence.ExploreDbContext>();
        await application.Database.OpenConnectionAsync(fixture.CancellationToken);
        await using var original = await application.Database.BeginTransactionAsync(fixture.CancellationToken);
        string currentPath = new SqliteConnectionStringBuilder(application.Database.GetConnectionString()).DataSource;
        string unavailableApplication = Path.Combine(Path.GetDirectoryName(currentPath)!,
            $"not_created_{Guid.CreateVersion7():N}", "application.sqlite");
        var factory = Create(fixture, scope, unavailableApplication);
        await using (var identity = await factory.CreateExternalIdentityAsync(fixture.CancellationToken))
        {
            var row = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleAsync(value => value.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(row.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
        }
        await Assert.That(File.Exists(unavailableApplication)).IsFalse();
        await Assert.That(application.Database.CurrentTransaction).IsSameReferenceAs(original);
        await Assert.That(application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        await original.RollbackAsync(fixture.CancellationToken);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task RepeatedOldPairDisposalCannotReturnOrCloseTheNextPooledOwner(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var factory = Create(fixture, scope);
        var previous = await factory.CreateAsync(null, fixture.CancellationToken);
        var previousContext = previous.Application;
        var previousId = previousContext.ContextId;
        await previous.DisposeAsync();
        await using var current = await factory.CreateAsync(null, fixture.CancellationToken);
        await Assert.That(current.Application).IsSameReferenceAs(previousContext);
        await Assert.That(current.Application.ContextId == previousId).IsFalse();
        await current.Application.Database.OpenConnectionAsync(fixture.CancellationToken);
        await using var transaction = await current.Application.Database.BeginTransactionAsync(fixture.CancellationToken);
        await previous.DisposeAsync();
        current.Application.RequireBoundTenantLease();
        await Assert.That(current.Application.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(current.Application.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Open);
        var row = await current.Identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(value => value.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(row.PersonalActorId).IsEqualTo(fixture.Receipt.PersonalActorId);
        await transaction.RollbackAsync(fixture.CancellationToken);
    }

    [Test]
    public async Task EmptyTenantCannotBeNormalizedIntoAnInstanceWriterSelection()
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(IdentityDatabaseTopology.Colocated);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var factory = Create(fixture, scope);
        await Assert.That(async () =>
        {
            await using var unexpected = await factory.CreateAsync(Guid.Empty, fixture.CancellationToken);
        }).Throws<ArgumentException>();
        await using var valid = await factory.CreateAsync(null, fixture.CancellationToken);
        valid.Application.RequireBoundTenantLease();
        await Assert.That(valid.Application.TenantFilterTenantId).IsNull();
    }

    private static LocalIdentityWriterDbContextFactory Create(LocalCredentialFirstUseTests.Fixture fixture,
        AsyncServiceScope scope, string? applicationPath = null)
    {
        var application = scope.ServiceProvider.GetRequiredService<Explore.Persistence.ExploreDbContext>();
        DbContext identity = fixture.Identity(scope);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = applicationPath ?? new SqliteConnectionStringBuilder(application.Database.GetConnectionString()).DataSource,
        };
        var external = ReferenceEquals(identity, application) ? null : new ExternalIdentityDatabaseDescriptor(
            PrimaryDatabaseProvider.Sqlite, identity.Database.GetConnectionString()!, IdentityDatabaseConfiguration.DefaultSchema, null, null);
        return new(options, external, poolSize: 1);
    }
}
