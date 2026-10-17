using System.Data;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PostgresAiActorContextReadContractTests
{
    [Test]
    public async Task PortablePreparedHumanWithoutHeldSourceAdmissionIsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection);
        Seed seed;
        string accountValue;
        await using (var setup = new ExploreDbContext(options.Options))
        {
            setup.EnableTenantFilterBypass("Portable actor context fixture seeding.");
            await setup.Database.EnsureCreatedAsync();
            await LookupTableSeeder.SeedAsync(setup);
            seed = await SeedRowsAsync(setup);
            var principal = Principal(seed.User);
            var account = principal.GetProviderIdentity()!.AccountKey;
            accountValue = account.Value;
            setup.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = seed.User,
                User = null!,
                AuthenticationProviderId = (int)account.ProviderKind,
                AuthenticationProvider = null!,
                ProviderKey = accountValue
            });
            await setup.SaveChangesAsync();
        }
        var resolver = new ResolveCurrentUserIdByIdentityRequestHandler(
            new ExternalLoginAuthenticationRepository(new PortableIdentityFactory(options.Options)));
        var subject = await HumanCommitSubject.ResolveAsync(Principal(seed.User), resolver, default)
            ?? throw new InvalidOperationException("Portable human must resolve through its persisted provider binding.");
        await Assert.That(subject.AccountKey!.Value).IsEqualTo(accountValue);
        await using var context = new ExploreDbContext(options.Options) { TenantContext = new SelectedTenant(seed.Tenant) };
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, seed.Tenant);
        lease.BindHuman(subject, HumanAdministratorCommitPurpose.RuntimeTenant, null);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new AiActorContextRepository(context, lease);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetAvailableActorsAsync());
        await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
        await owner.RollbackAsync();

        static ClaimsPrincipal Principal(Guid user) => new(new ClaimsIdentity(
            [new Claim("sub", user.ToString("D")), new Claim("auth_provider", "local"),
                new Claim("iss", LocalIdentityOptions.Issuer)], "fixture"));
    }

    /// <summary>Actual EF identity lookup over the open SQLite database, without an authority substitute.</summary>
    private sealed class PortableIdentityFactory(DbContextOptions<ExploreDbContext> options) : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new ExploreDbContext(options);
            context.BindTenantLease();
            return Task.FromResult(context);
        }
    }
}
