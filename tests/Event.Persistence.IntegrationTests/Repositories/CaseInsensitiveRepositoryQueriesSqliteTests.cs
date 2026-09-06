// ABOUTME: File-backed SQLite regressions for provider-neutral case-insensitive repository queries.
// ABOUTME: Proves privacy-provider equality and wildcard actor search without PostgreSQL ILIKE.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("PersistenceSqlite")]
public sealed class CaseInsensitiveRepositoryQueriesSqliteTests
{
    [Test]
    public async Task GetProviderCandidatesAsync_MixedCaseKeycloakProvider_ReturnsCandidate()
    {
        string databasePath = CreateDatabasePath();

        try
        {
            await using ExploreDbContext context = CreateContext(databasePath);
            await context.Database.MigrateAsync();

            var status = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "ACTIVE",
                FullName = "Active",
                IsActiveState = true
            };
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "SQLite privacy tenant",
                Slug = $"sqlite-privacy-{Guid.NewGuid():N}",
                TenantStatusId = status.Id,
                TenantStatus = status,
                CreatedAt = DateTime.UtcNow
            };
            var user = CreateUser("privacy");
            var provider = new AuthenticationProvider
            {
                Id = (int)"KeYcLoAk".ParseAuthenticationProviderKind(),
                MasterCode = "keycloak",
                FullName = "Keycloak"
            };
            var login = new UserExternalLogin { Id = Guid.CreateVersion7(),
                UserId = user.Id,
                User = user,
                AuthenticationProviderId = provider.Id,
                AuthenticationProvider = provider,
                ProviderKey = "sqlite-keycloak-subject",
                CreatedAt = DateTime.UtcNow
            };
            context.AddRange(status, tenant, user, provider, login);
            await context.SaveChangesAsync();

            var repository = new UserLocationPrivacyErasureRepository(context);
            IReadOnlyList<PrivacyErasureProviderCandidate> candidates =
                await repository.GetProviderCandidatesAsync(user.Id, CancellationToken.None);

            await Assert.That(candidates).HasSingleItem();
            await Assert.That(candidates[0].ProviderKind).IsEqualTo(PrivacyErasureProviderKind.Keycloak);
            await Assert.That(candidates[0].Locator).IsEqualTo(login.ProviderKey);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task SearchAiReferenceActorsAsync_MixedCaseWildcardPattern_ReturnsMatch()
    {
        string databasePath = CreateDatabasePath();

        try
        {
            await using ExploreDbContext context = CreateContext(databasePath);
            await context.Database.MigrateAsync();

            var actorType = new ActorType
            {
                Id = (int)ActorTypeEnum.User,
                MasterCode = "USER",
                FullName = "User"
            };
            var user = CreateUser("actor");
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(),
                ActorTypeId = actorType.Id,
                ActorType = actorType,
                UserId = user.Id,
                User = user,
                Pii = new ActorPii { DisplayName = "Muslim Community" },
                CreatedAt = DateTime.UtcNow,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            user.Actor = actor;
            context.Add(actor);
            await context.SaveChangesAsync();

            var repository = new ActorRepository(context);
            IReadOnlyList<Actor> matches = await repository.SearchAiReferenceActorsAsync(
                "MU%LIM",
                10,
                CancellationToken.None);

            await Assert.That(matches).HasSingleItem();
            await Assert.That(matches[0].Id).IsEqualTo(actor.Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static ExploreDbContext CreateContext(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        }.ToString();

        return new ExploreDbContext(
            new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite(connectionString, options =>
                    options.MigrationsAssembly("Explore.Persistence.Migrations.Sqlite"))
                .UseSnakeCaseNamingConvention()
                .Options);
    }

    private static User CreateUser(string suffix) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new UserPii
        {
            Email = $"{suffix}@example.invalid",
            FirstName = "SQLite",
            LastName = "Regression"
        },
        CreatedAt = DateTime.UtcNow,
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"event-case-insensitive-{Guid.NewGuid():N}.db");
}
