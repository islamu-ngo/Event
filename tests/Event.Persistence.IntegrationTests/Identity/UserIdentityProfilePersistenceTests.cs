using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class UserIdentityProfilePersistenceTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TrackedProfilePersistsWithoutRewritingAccountOwnershipOrDeletionFields(bool emailVerified)
    {
        await using var store = await Store.CreateAsync();
        IUserRepository repository = new UserRepository(store.Context);
        User user = (await repository.GetById(store.UserId))!;
        var original = (user.CreatedAt, user.CreatedBy, user.IsDeleted, user.DeletedAt, user.DeletedBy,
            user.LastActiveTenantId, user.Pii.FirstName, user.Pii.LastName);
        Guid stamp = user.ConcurrencyStamp;
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            user.Email = "synchronized@example.test";
            user.EmailVerified = emailVerified;
            await repository.SaveIdentityProfileAsync(user, Token);
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
        User user = (await repository.GetById(store.UserId))!;
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
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveIdentityProfileAsync(user, Token));
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
        User user = (await repository.GetById(store.UserId))!;
        Guid advanced = Guid.CreateVersion7();
        await store.Context.Users.Where(row => row.Id == store.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, advanced), Token);
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            user.EmailVerified = true;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.SaveIdentityProfileAsync(user, Token));
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
        User user = (await repository.GetById(store.UserId))!;
        string originalEmail = user.Email;
        bool? originalVerification = user.EmailVerified;
        Guid advanced = Guid.CreateVersion7();
        await store.Context.Users.Where(row => row.Id == store.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, advanced), Token);
        await using (var transaction = await store.Context.Database.BeginTransactionAsync(Token))
        {
            user.Email = "stale-email-only@example.test";
            await Assert.That(user.EmailVerified).IsEqualTo(originalVerification);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.SaveIdentityProfileAsync(user, Token));
            await transaction.RollbackAsync(Token);
        }
        store.Context.ChangeTracker.Clear();
        User persisted = (await repository.GetById(store.UserId))!;
        await Assert.That(persisted.Email).IsEqualTo(originalEmail);
        await Assert.That(persisted.EmailVerified).IsEqualTo(originalVerification);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(advanced);
    }

    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    private sealed class Store(SqliteConnection connection, ExploreDbContext context,
        HumanSecuritySourceGate humanGate, IAsyncDisposable human,
        EventDiscoveryEnrollmentGate discoveryGate, IAsyncDisposable discovery,
        Guid userId) : IAsyncDisposable
    {
        internal ExploreDbContext Context => context;
        internal Guid UserId => userId;

        internal static async Task<Store> CreateAsync()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Pooling = false
            }.ToString());
            await connection.OpenAsync(Token);
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            var context = new ExploreDbContext(options);
            context.BindTenantLease();
            var humanGate = new HumanSecuritySourceGate(context);
            IAsyncDisposable human = await humanGate.AcquireAsync(Token);
            var discoveryGate = new EventDiscoveryEnrollmentGate(context);
            IAsyncDisposable discovery = await discoveryGate.AcquireAsync(Token);
            Guid userId = Guid.CreateVersion7();
            var store = new Store(connection, context, humanGate, human, discoveryGate, discovery, userId);
            try
            {
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
            await discovery.DisposeAsync();
            await discoveryGate.DisposeAsync();
            await human.DisposeAsync();
            await humanGate.DisposeAsync();
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
