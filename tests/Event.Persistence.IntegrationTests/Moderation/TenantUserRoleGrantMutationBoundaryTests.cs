using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class TenantUserRoleGrantMutationBoundaryTests
{
    [Test]
    public async Task Grant_and_revoke_advance_qualification_only_with_successful_role_changes()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await SeedAdministratorAsync(database);
        await using var context = database.Context();
        var session = Session(database, context, database.AdministratorId);
        var repository = new TenantUserRoleGrantRepository(context);
        var create = new CreateTenantUserRoleGrantCommandHandler(repository,
            new TenantUserRepository(context), new RoleRepository(context), database, session.Boundary);
        var target = await context.TenantUsers.AsNoTracking().SingleAsync(row => row.UserId == database.SubjectId);
        context.ChangeTracker.Clear();
        var command = new CreateTenantUserRoleGrantCommand
        {
            TenantId = database.TenantId,
            TenantUserRoleGrantDto = new() { TenantUserId = target.Id, RoleId = (int)RoleEnum.TenantModerator }
        };
        var before = await RevisionAsync(context);
        var result = await create.ExecuteAsync(command, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        var grant = await context.TenantUserRoleGrants.AsNoTracking().SingleAsync(row => row.RoleId == (int)RoleEnum.TenantModerator);
        await Assert.That(grant.GrantedBy).IsEqualTo(database.AdministratorId);
        context.ChangeTracker.Clear();
        await Assert.That((await create.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        context.ChangeTracker.Clear();
        var revoke = new RevokeTenantUserRoleGrantCommandHandler(repository, session.Boundary);
        var removal = new RevokeTenantUserRoleGrantCommand { Id = grant.Id, TenantId = database.TenantId };
        await Assert.That(await revoke.ExecuteAsync(removal, CancellationToken.None)).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 2);
        context.ChangeTracker.Clear();
        await Assert.That(await revoke.ExecuteAsync(removal, CancellationToken.None)).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 2);
    }

    [Test]
    [Arguments("provider")]
    [Arguments("member")]
    [Arguments("foreign-target")]
    public async Task Native_boundary_does_not_borrow_provider_or_target_authority(string denial)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await SeedAdministratorAsync(database);
        await using var context = database.Context();
        var session = Session(database, context,
            denial == "member" ? database.SubjectId : database.AdministratorId, denial != "provider");
        var membership = await context.TenantUsers.AsNoTracking().SingleAsync(row => row.UserId == database.SubjectId);
        var before = await RevisionAsync(context);
        var invoked = false;
        await Assert.That(async () => await session.Boundary.ExecuteCreateAsync(database.TenantId,
            denial == "foreign-target" ? Guid.CreateVersion7() : membership.Id, (int)RoleEnum.TenantModerator,
            (_, _) => { invoked = true; return Task.FromResult(BaseCommandResponse.Success(Guid.CreateVersion7())); },
            CancellationToken.None)).Throws<AuthorizationException>();
        await Assert.That(invoked).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
        await Assert.That(await context.TenantUserRoleGrants.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_changed_caller_before_commit_rolls_back_role_and_qualification_together(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await SeedAdministratorAsync(database);
        await using var context = database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = Session(database, context, database.AdministratorId,
            privacy: privacy?.Authority(context, TimeProvider.System));
        var membership = await context.TenantUsers.AsNoTracking().SingleAsync(row => row.UserId == database.SubjectId);
        var before = await RevisionAsync(context);
        bool? privacyOwnedCommit = null;
        await Assert.That(async () => await session.Boundary.ExecuteCreateAsync(database.TenantId,
            membership.Id, (int)RoleEnum.TenantModerator, async (actor, token) =>
            {
                privacyOwnedCommit = context.IdentityFenceOwnsTransaction;
                var grant = Grant(database.TenantId, membership.Id, (int)RoleEnum.TenantModerator, actor);
                context.TenantUserRoleGrants.Add(grant);
                await context.SaveChangesAsync(token);
                session.Http.HttpContext!.User = Principal(database.SubjectId);
                return BaseCommandResponse.Success(grant.Id);
            }, CancellationToken.None)).Throws<AuthorizationException>();
        await Assert.That(privacyOwnedCommit).IsEqualTo(colocated);
        await Assert.That(await context.TenantUserRoleGrants.CountAsync()).IsEqualTo(1);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task An_authorized_self_revocation_commits_but_cannot_authorize_a_later_write()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var ownGrant = await SeedAdministratorAsync(database);
        await using var context = database.Context();
        var session = Session(database, context, database.AdministratorId);
        var handler = new RevokeTenantUserRoleGrantCommandHandler(new TenantUserRoleGrantRepository(context), session.Boundary);
        var before = await RevisionAsync(context);
        await Assert.That(await handler.ExecuteAsync(new() { Id = ownGrant, TenantId = database.TenantId }, CancellationToken.None)).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        context.ChangeTracker.Clear();
        await Assert.That(async () => await handler.ExecuteAsync(new() { Id = ownGrant, TenantId = database.TenantId },
            CancellationToken.None)).Throws<AuthorizationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retained_erasure_blocks_new_authority_even_before_primary_cleanup(bool erasedActor)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await SeedAdministratorAsync(database);
        await using var context = database.Context();
        await database.Privacy.Authority(context, TimeProvider.System).AppendAsync(PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            erasedActor ? database.AdministratorId : database.SubjectId,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        var session = Session(database, context, database.AdministratorId);
        var membership = await context.TenantUsers.AsNoTracking().SingleAsync(row => row.UserId == database.SubjectId);
        var before = await RevisionAsync(context);
        var invoked = false;
        await Assert.That(async () => await session.Boundary.ExecuteCreateAsync(database.TenantId, membership.Id,
            (int)RoleEnum.TenantModerator, (_, _) =>
            {
                invoked = true;
                return Task.FromResult(BaseCommandResponse.Success(Guid.CreateVersion7()));
            }, CancellationToken.None)).Throws<AuthorizationException>();
        await Assert.That(invoked).IsFalse();
        await Assert.That(await context.TenantUserRoleGrants.CountAsync()).IsEqualTo(1);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
    }

    [Test]
    [Arguments("create", false, false, true)]
    [Arguments("create", true, false, true)]
    [Arguments("revoke", false, false, true)]
    [Arguments("revoke", true, false, true)]
    [Arguments("self-revoke", false, false, true)]
    [Arguments("self-revoke", true, false, true)]
    [Arguments("create", true, true, true)]
    [Arguments("self-revoke", true, true, true)]
    [Arguments("create", false, false, false)]
    [Arguments("self-revoke", false, false, false)]
    public async Task Ambiguous_commit_reconciles_success_or_replays_only_a_rolled_back_attempt(
        string mutation, bool verify, bool rollBack, bool retry)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var ownGrant = await SeedAdministratorAsync(database);
        Guid targetGrant;
        Guid membership;
        await using (var seed = database.Context())
        {
            membership = await seed.TenantUsers.Where(row => row.UserId == database.SubjectId).Select(row => row.Id).SingleAsync();
            var grant = Grant(database.TenantId, membership, (int)RoleEnum.TenantModerator, database.AdministratorId);
            if (mutation == "revoke") { seed.Add(grant); await seed.SaveChangesAsync(); }
            targetGrant = mutation == "self-revoke" ? ownGrant : grant.Id;
        }
        var fault = new LostRoleCommit(verify, rollBack);
        await using var baseline = database.Context();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = database };
        var session = Session(database, context, database.AdministratorId);
        var repository = new TenantUserRoleGrantRepository(context);
        var before = await RevisionAsync(context);
        if (mutation == "create")
        {
            var handler = new CreateTenantUserRoleGrantCommandHandler(repository,
                new TenantUserRepository(context), new RoleRepository(context), database, session.Boundary);
            var result = await handler.ExecuteAsync(new()
            {
                TenantId = database.TenantId,
                TenantUserRoleGrantDto = new() { TenantUserId = membership, RoleId = (int)RoleEnum.TenantModerator }
            }, CancellationToken.None);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(await context.TenantUserRoleGrants.CountAsync(row => row.Id == result.Id)).IsEqualTo(1);
        }
        else
        {
            var handler = new RevokeTenantUserRoleGrantCommandHandler(repository, session.Boundary);
            await Assert.That(await handler.ExecuteAsync(new() { TenantId = database.TenantId, Id = targetGrant },
                CancellationToken.None)).IsTrue();
            await Assert.That(await context.TenantUserRoleGrants.Where(row => row.Id == targetGrant)
                .Select(row => row.RevokedBy).SingleAsync()).IsEqualTo(database.AdministratorId);
        }
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Non_retrying_rollback_preserves_failure_and_does_not_invent_success()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await SeedAdministratorAsync(database);
        await using var baseline = database.Context();
        var fault = new LostRoleCommit(verify: false, rollBack: true);
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = database };
        var session = Session(database, context, database.AdministratorId);
        var membership = await context.TenantUsers.AsNoTracking().Where(row => row.UserId == database.SubjectId)
            .Select(row => row.Id).SingleAsync();
        var grant = Grant(database.TenantId, membership, (int)RoleEnum.TenantModerator, database.AdministratorId);
        var before = await RevisionAsync(context);
        await Assert.That(async () => await session.Boundary.ExecuteCreateAsync(database.TenantId, membership,
            grant.RoleId, async (_, token) =>
            {
                context.Add(grant);
                await context.SaveChangesAsync(token);
                return BaseCommandResponse.Success(grant.Id);
            }, CancellationToken.None)).Throws<LostRoleCommitException>();
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.TenantUserRoleGrants.CountAsync(row => row.Id == grant.Id)).IsEqualTo(0);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task<Guid> SeedAdministratorAsync(SourceDatabase database)
    {
        await using var context = database.Context();
        var membership = await context.TenantUsers.SingleAsync(row => row.UserId == database.AdministratorId);
        var grant = Grant(database.TenantId, membership.Id, (int)RoleEnum.TenantAdmin, database.AdministratorId);
        context.TenantUserRoleGrants.Add(grant);
        await context.SaveChangesAsync();
        return grant.Id;
    }

    private static TenantUserRoleGrant Grant(Guid tenant, Guid membership, int role, Guid actor) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant, Tenant = null!, TenantUserId = membership, TenantUser = null!,
        RoleId = role, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = DateTime.UtcNow, GrantedBy = actor
    };

    private static Task<long> RevisionAsync(ExploreDbContext context) => context.ModerationQualificationEpochs.AsNoTracking()
        .Select(row => row.Revision).SingleAsync();

    private static ClaimsPrincipal Principal(Guid actor) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "tenant-role-witness"));

    private static (TenantUserRoleGrantMutationBoundary Boundary, HttpContextAccessor Http) Session(
        SourceDatabase database, ExploreDbContext context, Guid actor, bool allowed = true,
        IPrivacyIdentityFenceAuthority? privacy = null)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal(actor) } };
        var admin = new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context), new UserExternalLoginRepository(context),
            NullLogger<AdminContext>.Instance);
        return (new(context, privacy ?? database.Privacy.Authority(context, TimeProvider.System), new UserContext(http), admin,
            new MachinePrincipalAccessor(http), new Provider(context, allowed)), http);
    }

    internal sealed class LostRoleCommitException(bool verify) : TimeoutException("Role commit acknowledgement lost.")
    {
        public bool Verify { get; } = verify;
    }

    internal sealed class LostRoleCommit(bool verify, bool rollBack) : DbTransactionInterceptor
    {
        public bool Injected { get; private set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (rollBack && !Injected)
            {
                Injected = true;
                await transaction.RollbackAsync(cancellationToken);
                throw new LostRoleCommitException(verify);
            }
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Injected)
            {
                Injected = true;
                throw new LostRoleCommitException(verify);
            }
            return Task.CompletedTask;
        }
    }

    // Fault-only SQLite retry policy; the role, privacy and transaction owners remain real.
    internal sealed class RoleCommitRetryFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RoleCommitRetry(dependencies);
    }

    private sealed class RoleCommitRetry(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is LostRoleCommitException;
        protected override bool ShouldVerifySuccessOn(Exception exception) => exception is LostRoleCommitException { Verify: true };
    }

    private sealed class Provider(ExploreDbContext context, bool allowed) : IAuthorizationProvider
    {
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Provider calls must precede the protected database unit.");
            return Task.FromResult(allowed ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local));
        }

        public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default)
        {
            var decisions = new List<AuthorizationDecision>();
            foreach (var request in requests) decisions.Add(await AuthorizeAsync(request, cancellationToken));
            return decisions;
        }
    }
}
