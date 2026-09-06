// ABOUTME: PostgreSQL characterization of the tenant-filtered token lookup used by current User deletion.
// ABOUTME: Proves a second-tenant session remains outside the ordinary repository result for the same User.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<DeleteUserTokenVisibilityPostgreSqlFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class DeleteUserTokenVisibilityCharacterizationTests(DeleteUserTokenVisibilityPostgreSqlFixture fixture)
{
    [Test]
    public async Task CurrentDeleteTokenLookupLeavesTheSameUsersOtherTenantSessionOutsideItsResult()
    {
        Guid tenantAId;
        Guid tenantBId;
        Guid userId;
        Guid tenantATokenId;
        Guid tenantBTokenId;

        await using (var seedContext = fixture.CreateDbContext())
        {
            var activeStatus = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "ACTIVE",
                FullName = "Active",
                IsActiveState = true
            };
            var tenantA = CreateTenant("delete-token-a", activeStatus);
            var tenantB = CreateTenant("delete-token-b", activeStatus);
            var user = CreateUser();
            var tenantAToken = CreateToken(user, tenantA, "did:plc:tenant-a");
            var tenantBToken = CreateToken(user, tenantB, "did:plc:tenant-b");

            seedContext.AddRange(activeStatus, tenantA, tenantB, user, tenantAToken, tenantBToken);
            await seedContext.SaveChangesAsync();

            tenantAId = tenantA.Id;
            tenantBId = tenantB.Id;
            userId = user.Id;
            tenantATokenId = tenantAToken.Id;
            tenantBTokenId = tenantBToken.Id;
        }

        await using (var tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantAId)))
        {
            var repository = new UserAuthenticationTokenRepository(tenantAContext);
            List<UserAuthenticationToken> currentDeleteLookup = await repository.GetByUser(userId);

            await Assert.That(currentDeleteLookup.Select(token => token.Id)).IsEquivalentTo([tenantATokenId]);
            await Assert.That(currentDeleteLookup.Any(token => token.TenantId == tenantBId)).IsFalse();
        }

        await using var verificationContext = fixture.CreateDbContext();
        Guid[] durableTokenIds = await verificationContext.UserAuthenticationTokens
            .Where(token => token.UserId == userId)
            .OrderBy(token => token.Id)
            .Select(token => token.Id)
            .ToArrayAsync();
        await Assert.That(durableTokenIds).IsEquivalentTo([tenantATokenId, tenantBTokenId]);
    }

    private static Tenant CreateTenant(string slugPrefix, TenantStatus activeStatus) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FullName = slugPrefix,
            Slug = $"{slugPrefix}-{Guid.NewGuid():N}"[..32],
            TenantStatusId = activeStatus.Id,
            TenantStatus = activeStatus
        };

    private static User CreateUser()
    {
        Guid userId = Guid.CreateVersion7();
        return new User
        {
            Id = userId,
            Pii = new UserPii
            {
                UserId = userId,
                Email = "delete-token-characterization@example.invalid",
                FirstName = "Delete",
                LastName = "Characterization"
            }
        };
    }

    private static UserAuthenticationToken CreateToken(User user, Tenant tenant, string subjectDid) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            TenantId = tenant.Id,
            Tenant = tenant,
            Provider = "atproto",
            SubjectDid = subjectDid,
            SessionCiphertext = Enumerable.Repeat((byte)7, 29).ToArray(),
            EncryptionKeyId = "characterization-key",
            OAuthClientKeyId = "characterization-client",
            EnvelopeVersion = 1,
            ConcurrencyStamp = Guid.CreateVersion7(),
            PdsHost = "https://pds.example/"
        };

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

public sealed class DeleteUserTokenVisibilityPostgreSqlFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("delete_user_token_visibility")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using ExploreDbContext context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public ExploreDbContext CreateDbContext()
    {
        ExploreDbContext context = CreateContext();
        context.EnableTenantFilterBypass("Delete User token visibility characterization seed context.");
        return context;
    }

    public ExploreDbContext CreateTenantFilteredDbContext(ITenantContext tenantContext)
    {
        ExploreDbContext context = CreateContext();
        context.TenantContext = tenantContext;
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    private ExploreDbContext CreateContext()
    {
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }
}
