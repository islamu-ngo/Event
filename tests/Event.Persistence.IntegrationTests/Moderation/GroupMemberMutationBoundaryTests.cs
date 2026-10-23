using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.GroupMembers.Handlers.Commands;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class GroupMemberMutationBoundaryTests
{
    [Test]
    public async Task Last_administrator_successor_and_noop_transitions_preserve_membership_and_epoch_together()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (administrator, target, _) = await SeedAsync(database);
        await using var context = database.Context();
        var session = Session(database, context, database.AdministratorId);
        var members = new GroupMemberRepository(context);
        var tenant = database;
        var update = new UpdateGroupMemberRoleCommandHandler(members, new UserContext(session.Http), tenant, session.Boundary);
        var delete = new DeleteGroupMemberCommandHandler(members, new UserContext(session.Http), tenant, session.Boundary);
        var revision = await RevisionAsync(context);
        var demotion = new UpdateGroupMemberRoleCommand { UpdateGroupMemberRoleDto = new() { Id = administrator, Role = RoleEnum.GroupMember } };
        await Assert.That((await update.ExecuteAsync(demotion, default)).IsSuccess).IsFalse();
        await Assert.That((await delete.ExecuteAsync(new() { MemberId = administrator }, default)).IsSuccess).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision);
        var promotion = new UpdateGroupMemberRoleCommand { UpdateGroupMemberRoleDto = new() { Id = target, Role = RoleEnum.GroupAdmin } };
        await Assert.That((await update.ExecuteAsync(promotion, default)).IsSuccess).IsTrue();
        await Assert.That((await update.ExecuteAsync(promotion, default)).IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision + 1);
        await Assert.That((await update.ExecuteAsync(demotion, default)).IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision + 2);
        await Assert.That(async () => await delete.ExecuteAsync(new() { MemberId = administrator }, default))
            .Throws<Explore.Application.Authorization.AuthorizationException>();
        session.Http.HttpContext!.User = Principal(database.SubjectId);
        await Assert.That((await delete.ExecuteAsync(new() { MemberId = target }, default)).IsSuccess).IsFalse();
        await Assert.That((await delete.ExecuteAsync(new() { MemberId = administrator }, default)).IsSuccess).IsTrue();
        await Assert.That(await context.GroupMembers.Select(row => row.Id).ToArrayAsync()).IsEquivalentTo([target]);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision + 3);
    }

    [Test]
    public async Task Addition_preserves_exact_participation_position_and_duplicate_protection()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, _, groupId) = await SeedAsync(database);
        await using var context = database.Context();
        var session = Session(database, context, database.AdministratorId);
        var add = new AddGroupMemberCommandHandler(new GroupRepository(context), new GroupTenantRepository(context),
            new GroupMemberRepository(context), new UserRepository(context), new UserContext(session.Http),
            database, session.Boundary);
        var email = await context.Users.Where(row => row.Id == database.ReviewerId).Select(row => row.Pii!.Email).SingleAsync();
        var revision = await RevisionAsync(context);
        var command = new AddGroupMemberCommand
        {
            AddGroupMemberDto = new()
            { GroupId = groupId, Email = email!, Role = RoleEnum.GroupMember, GroupPositionId = 4 }
        };
        await Assert.That(async () => await add.ExecuteAsync(command with
        { AddGroupMemberDto = command.AddGroupMemberDto with { GroupId = Guid.CreateVersion7() } }, default))
            .Throws<Explore.Application.Authorization.AuthorizationException>();
        await Assert.That((await add.ExecuteAsync(command with
        { AddGroupMemberDto = command.AddGroupMemberDto with { Email = "missing@example.test" } }, default)).IsSuccess).IsFalse();
        var created = await add.ExecuteAsync(command, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var persisted = await context.GroupMembers.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        await Assert.That(persisted.UserId).IsEqualTo(database.ReviewerId);
        await Assert.That(persisted.TenantId).IsEqualTo(database.TenantId);
        await Assert.That(persisted.GroupPositionId).IsEqualTo((int?)4);
        await Assert.That(persisted.GroupTenantId).IsEqualTo(await context.GroupTenants.Select(row => row.Id).SingleAsync());
        await Assert.That((await add.ExecuteAsync(command, default)).IsSuccess).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision + 1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Provider_or_ordinary_member_denial_cannot_enter_the_callback(bool deniedProvider)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target, _) = await SeedAsync(database);
        await using var context = database.Context();
        var session = Session(database, context, deniedProvider ? database.AdministratorId : database.SubjectId, !deniedProvider);
        var revision = await RevisionAsync(context);
        var entered = false;
        await Assert.That(async () => await session.Boundary.ExecuteRoleChangeAsync(target, (int)RoleEnum.GroupAdmin,
            (_, _) => { entered = true; throw new InvalidOperationException("Unauthorized mutation entered."); }, default))
            .Throws<Explore.Application.Authorization.AuthorizationException>();
        await Assert.That(entered).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision);
    }

    [Test]
    public async Task A_group_administrator_does_not_require_an_optional_permission_lookup_row()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target, _) = await SeedAsync(database);
        await using var context = database.Context();
        var lookups = await context.Permissions.Where(row => row.MasterCode == PermissionCodes.GroupMemberUpdate).ToArrayAsync();
        foreach (var lookup in lookups) lookup.MasterCode = $"unused-{Guid.CreateVersion7():N}";
        await context.SaveChangesAsync();
        var session = Session(database, context, database.AdministratorId);
        var revision = await RevisionAsync(context);
        var handler = new UpdateGroupMemberRoleCommandHandler(new GroupMemberRepository(context),
            new UserContext(session.Http), database, session.Boundary);
        var result = await handler.ExecuteAsync(new()
        {
            UpdateGroupMemberRoleDto = new()
            { Id = target, Role = RoleEnum.GroupAdmin }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision + 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retained_erasure_prevents_new_group_authority_before_primary_cleanup(bool erasedActor)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target, _) = await SeedAsync(database);
        await using var context = database.Context();
        await database.Privacy.Authority(context, TimeProvider.System).AppendAsync(
            PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                erasedActor ? database.AdministratorId : database.SubjectId,
                PrivacyErasureReasonCode.SubjectErasureRequest, 1), default);
        var session = Session(database, context, database.AdministratorId);
        var revision = await RevisionAsync(context);
        var entered = false;
        await Assert.That(async () => await session.Boundary.ExecuteRoleChangeAsync(target, (int)RoleEnum.GroupAdmin,
            (_, _) => { entered = true; throw new InvalidOperationException("Erased authority entered."); }, default))
            .Throws<Explore.Application.Authorization.AuthorizationException>();
        await Assert.That(entered).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Changed_caller_rolls_back_group_authority_and_epoch_at_the_actual_commit(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target, _) = await SeedAsync(database);
        await using var context = database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = CreateBoundary(context, (privacy ?? database.Privacy).Authority(context, TimeProvider.System), database.AdministratorId);
        var revision = await RevisionAsync(context);
        bool? ownsCommit = null;
        await Assert.That(async () => await session.Boundary.ExecuteRoleChangeAsync(target, (int)RoleEnum.GroupAdmin,
            async (_, token) =>
            {
                ownsCommit = context.IdentityFenceOwnsTransaction;
                var member = await context.GroupMembers.SingleAsync(row => row.Id == target, token);
                member.RoleId = (int)RoleEnum.GroupAdmin;
                await context.SaveChangesAsync(token);
                session.Http.HttpContext!.User = Principal(database.SubjectId);
                return BaseCommandResponse.Success(target);
            }, default)).Throws<Explore.Application.Authorization.AuthorizationException>();
        await Assert.That(ownsCommit).IsEqualTo(colocated);
        await Assert.That(await context.GroupMembers.Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.GroupMember);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task Self_demotion_recovers_only_its_exact_committed_result(bool verify, bool rollBack, bool retry)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (administrator, target, _) = await SeedAsync(database);
        await using (var seed = database.Context())
        {
            var successor = await seed.GroupMembers.SingleAsync(row => row.Id == target);
            successor.RoleId = (int)RoleEnum.GroupAdmin;
            await seed.SaveChangesAsync();
        }
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, rollBack);
        await using var baseline = database.Context();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = database };
        var session = Session(database, context, database.AdministratorId);
        var handler = new UpdateGroupMemberRoleCommandHandler(new GroupMemberRepository(context),
            new UserContext(session.Http), database, session.Boundary);
        var revision = await RevisionAsync(context);
        var result = await handler.ExecuteAsync(new()
        {
            UpdateGroupMemberRoleDto = new()
            { Id = administrator, Role = RoleEnum.GroupMember }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(administrator);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.GroupMembers.Where(row => row.Id == administrator).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.GroupMember);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task<(Guid Administrator, Guid Target, Guid Group)> SeedAsync(SourceDatabase database)
    {
        await using var context = database.Context();
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Native group authority" };
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            Group = group,
            GroupId = group.Id,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        GroupMember Member(Guid user, RoleEnum role) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            GroupTenant = participation,
            GroupTenantId = participation.Id,
            UserId = user,
            User = null!,
            RoleId = (int)role,
            Role = null!
        };
        var administrator = Member(database.AdministratorId, RoleEnum.GroupAdmin);
        var target = Member(database.SubjectId, RoleEnum.GroupMember);
        context.AddRange(group, participation, administrator, target);
        if (!await context.Set<GroupPosition>().AnyAsync(row => row.Id == 4))
            context.Add(new GroupPosition { Id = 4, MasterCode = "member", FullName = "Member" });
        await context.SaveChangesAsync();
        return (administrator.Id, target.Id, group.Id);
    }

    private static Task<long> RevisionAsync(ExploreDbContext context) => context.ModerationQualificationEpochs.AsNoTracking()
        .Select(row => row.Revision).SingleAsync();
    private static ClaimsPrincipal Principal(Guid actor) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "group-membership-witness"));
    private static (GroupMemberMutationBoundary Boundary, HttpContextAccessor Http) Session(
        SourceDatabase database, ExploreDbContext context, Guid actor, bool allowed = true) =>
        CreateBoundary(context, database.Privacy.Authority(context, TimeProvider.System), actor, allowed);

    internal static (GroupMemberMutationBoundary Boundary, HttpContextAccessor Http) CreateBoundary(
        ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, Guid actor, bool allowed = true)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal(actor) } };
        var members = new GroupMemberRepository(context);
        var admin = new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), members, new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        return (new(context, privacy, new UserContext(http), admin, new MachinePrincipalAccessor(http), new Provider(context, allowed), members), http);
    }

    private sealed class Provider(ExploreDbContext context, bool allowed) : IAuthorizationProvider
    {
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            if (context.Database.CurrentTransaction is not null) throw new InvalidOperationException("Provider called inside primary transaction.");
            return Task.FromResult(allowed ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local));
        }
        public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default)
        {
            var results = new List<AuthorizationDecision>();
            foreach (var request in requests) results.Add(await AuthorizeAsync(request, cancellationToken));
            return results;
        }
    }
}
