using System.Data;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PostgresAiActorContextReadContractTests
{
    [Test]
    public async Task ResolvedHumanWithoutHeldSourcesCannotBypassRepository()
    {
        var seed = await SeedAsync();
        var subject = await ResolveSubjectAsync(seed);
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, seed.Tenant);
        lease.BindHuman(subject, HumanAdministratorCommitPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        var repository = new AiActorContextRepository(context, lease);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetAvailableActorsAsync());
            await using var replacement = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
            var transplanted = new AiActorContextRepository(replacement, lease);
            await Assert.ThrowsAsync<InvalidOperationException>(() => transplanted.GetAvailableActorsAsync());
            await owner.RollbackAsync();
        }
    }

    [Test]
    public async Task BoundHumanCannotTurnActorSelectionIntoBypassedTenantRead()
    {
        var seed = await SeedAsync();
        var subject = await ResolveSubjectAsync(seed);
        await using var context = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await context.Database.OpenConnectionAsync();
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, seed.Tenant);
        lease.BindHuman(subject, HumanAdministratorCommitPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new AiActorContextRepository(context, lease);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetAvailableActorsAsync());
        context.EnableTenantFilterBypass("Adversarial actor context owner replacement.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetAvailableActorsAsync());
        await owner.RollbackAsync();
    }

    [Test]
    public async Task CommittedMembershipRevocationIsVisibleOnNextNativeOwnerWithoutCachedActors()
    {
        var seed = await SeedAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, seed.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await using var oldOwner = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That((await ReadAsync(runtime, seed)).Count).IsEqualTo(3);
        await using var writer = fixture.CreateDbContext();
        await writer.Database.OpenConnectionAsync();
        await using var writeOwner = await writer.Database.BeginTransactionAsync();
        var connector = (NpgsqlConnection)writer.Database.GetDbConnection();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNotice(object sender, NpgsqlNoticeEventArgs args)
        {
            if (args.Notice.MessageText == "actor_membership_revocation_written") changed.TrySetResult();
        }
        connector.Notice += OnNotice;
        try
        {
            var sql = new HumanPrincipalSourceSql(writer);
            // Subscribe before dispatch. The notice proves the real UPDATE executed;
            // command completion and CommitAsync, not a delay, establish visibility.
            Task write = writer.Database.ExecuteSqlRawAsync($$"""
                UPDATE {{sql.Table(typeof(GroupMember))}} SET {{sql.Column(typeof(GroupMember), "IsDeleted")}} = TRUE
                    WHERE {{sql.Column(typeof(GroupMember), "Id")}} = {0};
                DO $changed$ BEGIN RAISE NOTICE 'actor_membership_revocation_written'; END $changed$;
                """, seed.GroupMember);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await write.WaitAsync(TimeSpan.FromSeconds(15));
            await writeOwner.CommitAsync();
            // Raw data capability tests deliberately hold no human S/P admission.
            // A Serializable snapshot is not terminal human authority; the parent
            // owns S/P fencing and revalidation before releasing a repository result.
            await Assert.That((await ReadAsync(runtime, seed)).Count).IsEqualTo(3);
            await oldOwner.RollbackAsync();
            await Assert.That((await ReadFreshAsync(seed)).Count).IsEqualTo(2);
        }
        finally { connector.Notice -= OnNotice; }
    }

    private async Task<HumanCommitSubject> ResolveSubjectAsync(Seed seed)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", seed.User.ToString("D")), new Claim("auth_provider", "local"),
                new Claim("iss", LocalIdentityOptions.Issuer)], "fixture"));
        var account = principal.GetProviderIdentity()!.AccountKey;
        await using (var migration = fixture.CreateDbContext())
        {
            migration.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = seed.User,
                User = null!,
                AuthenticationProviderId = (int)account.ProviderKind,
                AuthenticationProvider = null!,
                ProviderKey = account.Value
            });
            await migration.SaveChangesAsync();
        }
        var resolver = new ResolveCurrentUserIdByIdentityRequestHandler(new ExternalLoginAuthenticationRepository(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global))));
        var subject = await HumanCommitSubject.ResolveAsync(principal, resolver, default)
            ?? throw new InvalidOperationException("The persisted provider account must resolve the seeded human.");
        await Assert.That(subject.PrincipalId).IsEqualTo(seed.User);
        return subject;
    }
}
