using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class UserIdentityEmailOwnershipTests
{
    private static readonly DateTime ObservedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task DuplicateVerifiedOwnershipRollsBackTheLosingAccount()
    {
        await using var fixture = await Fixture.CreateAsync();
        User owner = await fixture.CreateUserAsync();
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await fixture.Repository.CreateClaimAsync(
                UserIdentityEmailClaim.Create(owner.Id, "shared@example.test"), CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }

        Guid losingId;
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            User losing = await fixture.CreateUserAsync();
            losingId = losing.Id;
            await Assert.That(async () => await fixture.Repository.CreateClaimAsync(
                    UserIdentityEmailClaim.Create(losingId, "shared@example.test"), CancellationToken))
                .Throws<DbUpdateException>();
            await transaction.RollbackAsync(CancellationToken);
        }
        fixture.Context.ChangeTracker.Clear();

        await Assert.That((await fixture.Repository.GetByNormalizedEmailAsync(
            "shared@example.test", CancellationToken))!.UserId).IsEqualTo(owner.Id);
        await Assert.That(await new UserRepository(fixture.Context).GetById(losingId)).IsNull();
    }

    [Test]
    public async Task RemovingOneProofPreservesIndependentEvidenceUntilTheLastProofIsGone()
    {
        await using var fixture = await Fixture.CreateAsync();
        User owner = await fixture.CreateUserAsync();
        UserExternalLogin first = await fixture.CreateBindingAsync(owner);
        UserExternalLogin second = await fixture.CreateBindingAsync(owner);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        UserIdentityEmailClaim claim = await fixture.Repository.CreateClaimAsync(
            UserIdentityEmailClaim.Create(owner.Id, "supported@example.test"), CancellationToken);
        await fixture.Repository.CreateEvidenceAsync(
            UserIdentityEmailEvidence.Create(owner.Id, claim.Id, first.Id, ObservedAt), CancellationToken);
        await fixture.Repository.CreateEvidenceAsync(
            UserIdentityEmailEvidence.Create(owner.Id, claim.Id, second.Id, ObservedAt), CancellationToken);

        await fixture.Repository.RemoveEvidenceByBindingAsync(first.Id, CancellationToken);

        await Assert.That(await fixture.Repository.GetEvidenceByBindingAsync(first.Id, CancellationToken)).IsNull();
        await Assert.That((await fixture.Repository.GetEvidenceByBindingAsync(second.Id, CancellationToken))!.IsActive)
            .IsTrue();
        await Assert.That((await fixture.Repository.GetByNormalizedEmailAsync(
            "supported@example.test", CancellationToken))!.UserId).IsEqualTo(owner.Id);

        await fixture.Repository.RemoveEvidenceByBindingAsync(second.Id, CancellationToken);

        await Assert.That(await fixture.Repository.GetByNormalizedEmailAsync("supported@example.test", CancellationToken))
            .IsNull();
        await transaction.CommitAsync(CancellationToken);
    }

    [Test]
    public async Task DatabaseRejectsProofFromAnotherAccountsBinding()
    {
        await using var fixture = await Fixture.CreateAsync();
        User owner = await fixture.CreateUserAsync();
        User other = await fixture.CreateUserAsync();
        UserExternalLogin otherBinding = await fixture.CreateBindingAsync(other);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        UserIdentityEmailClaim claim = await fixture.Repository.CreateClaimAsync(
            UserIdentityEmailClaim.Create(owner.Id, "owned@example.test"), CancellationToken);

        await Assert.That(async () => await fixture.Repository.CreateEvidenceAsync(
                UserIdentityEmailEvidence.Create(owner.Id, claim.Id, otherBinding.Id, ObservedAt), CancellationToken))
            .Throws<DbUpdateException>();
        await transaction.RollbackAsync(CancellationToken);
        fixture.Context.ChangeTracker.Clear();

        await Assert.That(await fixture.Repository.GetEvidenceByBindingAsync(otherBinding.Id, CancellationToken))
            .IsNull();
    }

    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    private sealed class Fixture(SqliteConnection connection, ExploreDbContext context) : IAsyncDisposable
    {
        public ExploreDbContext Context { get; } = context;
        public UserIdentityEmailRepository Repository { get; } = new(context);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Pooling = false
            }.ToString());
            await connection.OpenAsync(CancellationToken);
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, CancellationToken);
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection)
                .UseSnakeCaseNamingConvention()
                .Options;
            return new Fixture(connection, new ExploreDbContext(options));
        }

        public Task<User> CreateUserAsync() => new UserRepository(Context).Create(new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = "shared-contact@example.test",
                FirstName = "Identity",
                LastName = "Owner"
            },
            EmailVerified = true
        });

        public Task<UserExternalLogin> CreateBindingAsync(User owner) =>
            new UserExternalLoginRepository(Context).Create(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = owner.Id,
                User = owner,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = $"test:{Guid.CreateVersion7():D}"
            });

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
