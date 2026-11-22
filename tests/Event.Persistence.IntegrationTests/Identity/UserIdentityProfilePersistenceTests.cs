using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class UserIdentityProfilePersistenceTests
{
    [Test]
    [Arguments(true, false)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(false, true)]
    public async Task TrackedProfilePersistsWithoutRewritingAccountOwnershipOrDeletionFields(
        bool emailVerified, bool preparedMirror)
    {
        await using var store = await Store.CreateAsync();
        IUserRepository repository = new UserRepository(store.Context);
        User before = (await repository.GetById(store.UserId))!;
        var original = (before.CreatedAt, before.CreatedBy, before.IsDeleted, before.DeletedAt, before.DeletedBy,
            before.LastActiveTenantId, before.Pii.FirstName, before.Pii.LastName);
        Guid stamp = before.ConcurrencyStamp;
        store.Context.ChangeTracker.Clear();
        User user = (await repository.GetIdentityAccountForSynchronizationAsync(store.UserId, Token))!;
        await Assert.That(user.Pii).IsNull();
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            if (preparedMirror)
            {
                var writer = new IdentityEmailMirrorWriter(store.Context, repository);
                await using var mirror = await writer.PrepareAsync(store.UserId, Token);
                await mirror.ApplyAsync("synchronized@example.test", emailVerified, Token);
            }
            else
            {
                await repository.SaveIdentityProfileAsync(user, "synchronized@example.test", emailVerified, Token);
            }
            await Assert.That(store.Context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.CommitAsync(Token);
        }
        store.Context.ChangeTracker.Clear();
        User persisted = (await repository.GetById(store.UserId))!;
        await Assert.That(persisted.Email).IsEqualTo("synchronized@example.test");
        await Assert.That(persisted.EmailVerified).IsEqualTo(emailVerified);
        await Assert.That((persisted.CreatedAt, persisted.CreatedBy, persisted.IsDeleted, persisted.DeletedAt,
            persisted.DeletedBy, persisted.LastActiveTenantId, persisted.Pii.FirstName, persisted.Pii.LastName))
            .IsEqualTo(original);
        await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(stamp);
        await Assert.That(persisted.UpdatedAt).IsNotNull();
    }

    [Test]
    [Arguments("detached")]
    [Arguments("added")]
    [Arguments("deleted")]
    public async Task NonOriginalExistingProfileCannotBeAttachedOrPersisted(string state)
    {
        await using var store = await Store.CreateAsync();
        var repository = new UserRepository(store.Context);
        User user = (await repository.GetIdentityAccountForSynchronizationAsync(store.UserId, Token))!;
        Guid stamp = user.ConcurrencyStamp;
        store.Context.Entry(user).State = state switch
        {
            "added" => EntityState.Added,
            "deleted" => EntityState.Deleted,
            _ => EntityState.Detached
        };
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            user.EmailVerified = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SaveIdentityProfileAsync(user, "synchronized@example.test", true, Token));
            await transaction.RollbackAsync(Token);
        }
        store.Context.ChangeTracker.Clear();
        User persisted = (await repository.GetById(store.UserId))!;
        await Assert.That(persisted.EmailVerified).IsEqualTo(false);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(persisted.IsDeleted).IsFalse();
    }

    [Test]
    public async Task StaleProfileCannotOverwriteAnAlreadyAdvancedAccountVersion()
    {
        await using var store = await Store.CreateAsync();
        var repository = new UserRepository(store.Context);
        User user = (await repository.GetIdentityAccountForSynchronizationAsync(store.UserId, Token))!;
        Guid advanced = Guid.CreateVersion7();
        await store.Context.Users.Where(row => row.Id == store.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, advanced), Token);
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            user.EmailVerified = true;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                repository.SaveIdentityProfileAsync(user, null, true, Token));
            await transaction.RollbackAsync(Token);
        }
        store.Context.ChangeTracker.Clear();
        User persisted = (await repository.GetById(store.UserId))!;
        await Assert.That(persisted.EmailVerified).IsEqualTo(false);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(advanced);
    }

    [Test]
    public async Task EmailOnlyProfileCannotOverwriteAnAlreadyAdvancedAccountVersion()
    {
        await using var store = await Store.CreateAsync();
        IUserRepository repository = new UserRepository(store.Context);
        User user = (await repository.GetIdentityAccountForSynchronizationAsync(store.UserId, Token))!;
        string originalEmail = await store.Context.UserPii.Where(row => row.UserId == store.UserId)
            .Select(row => row.Email).SingleAsync(Token);
        bool? originalVerification = user.EmailVerified;
        Guid advanced = Guid.CreateVersion7();
        await store.Context.Users.Where(row => row.Id == store.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, advanced), Token);
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            await Assert.That(user.EmailVerified).IsEqualTo(originalVerification);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                repository.SaveIdentityProfileAsync(user, "stale-email-only@example.test", null, Token));
            await transaction.RollbackAsync(Token);
        }
        store.Context.ChangeTracker.Clear();
        User persisted = (await repository.GetById(store.UserId))!;
        await Assert.That(persisted.Email).IsEqualTo(originalEmail);
        await Assert.That(persisted.EmailVerified).IsEqualTo(originalVerification);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(advanced);
    }

    [Test]
    public async Task MissingObservationsPreserveTheAccountVersionAndEmailMirror()
    {
        await using var store = await Store.CreateAsync();
        IUserRepository repository = new UserRepository(store.Context);
        User user = (await repository.GetIdentityAccountForSynchronizationAsync(store.UserId, Token))!;
        Guid stamp = user.ConcurrencyStamp;
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            await repository.SaveIdentityProfileAsync(user, null, null, Token);
            await Assert.That(store.Context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.CommitAsync(Token);
        }
        store.Context.ChangeTracker.Clear();
        User persisted = (await repository.GetById(store.UserId))!;
        await Assert.That(persisted.Email).IsEqualTo("original@example.test");
        await Assert.That(persisted.EmailVerified).IsEqualTo(false);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    public async Task UnrelatedAccountMutationCannotBeSavedThroughIdentitySynchronization()
    {
        await using var store = await Store.CreateAsync();
        IUserRepository repository = new UserRepository(store.Context);
        User user = (await repository.GetIdentityAccountForSynchronizationAsync(store.UserId, Token))!;
        Guid stamp = user.ConcurrencyStamp;
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            user.LastActiveTenantId = Guid.CreateVersion7();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SaveIdentityProfileAsync(user, "synchronized@example.test", true, Token));
            await transaction.RollbackAsync(Token);
        }
        store.Context.ChangeTracker.Clear();
        User persisted = (await repository.GetById(store.UserId))!;
        await Assert.That(persisted.Email).IsEqualTo("original@example.test");
        await Assert.That(persisted.EmailVerified).IsEqualTo(false);
        await Assert.That(persisted.LastActiveTenantId).IsNull();
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(stamp);
    }

    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    private sealed class Store(SqliteConnection connection, ExploreDbContext context, Guid userId) : IAsyncDisposable
    {
        private HumanSecuritySourceGate? humanGate;
        private IAsyncDisposable? human;
        private EventDiscoveryEnrollmentGate? discoveryGate;
        private IAsyncDisposable? discovery;

        internal ExploreDbContext Context => context;
        internal Guid UserId => userId;

        internal static async Task<Store> CreateAsync()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Pooling = false
            }.ToString());
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            var context = new ExploreDbContext(options);
            Guid userId = Guid.CreateVersion7();
            var store = new Store(connection, context, userId);
            try
            {
                await connection.OpenAsync(Token);
                // Profile mirrors have no lookup dependencies. Retain both native schemas
                // without bootstrapping the unrelated application lookup catalogue.
                await context.Database.EnsureCreatedAsync(Token);
                var authorityOptions = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                    .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
                await using (var authority = new EmbeddedPrivacyErasureAuthorityDbContext(authorityOptions))
                    await authority.Database.ExecuteSqlRawAsync(authority.Database.GenerateCreateScript(), Token);
                context.BindTenantLease();
                store.humanGate = new HumanSecuritySourceGate(context);
                store.human = await store.humanGate.AcquireAsync(Token);
                store.discoveryGate = new EventDiscoveryEnrollmentGate(context);
                store.discovery = await store.discoveryGate.AcquireAsync(Token);
                await new UserRepository(context).Create(new User
                {
                    Id = userId,
                    Pii = new UserPii
                    {
                        UserId = userId,
                        Email = "original@example.test",
                        FirstName = "Profile",
                        LastName = "Owner"
                    },
                    EmailVerified = false,
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
                context.ChangeTracker.Clear();
                return store;
            }
            catch
            {
                await store.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (discovery is not null)
                await discovery.DisposeAsync();
            if (discoveryGate is not null)
                await discoveryGate.DisposeAsync();
            if (human is not null)
                await human.DisposeAsync();
            if (humanGate is not null)
                await humanGate.DisposeAsync();
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
