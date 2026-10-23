using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Handlers.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class OrganizationMemberMutationBoundaryTests
{
    [Test]
    public async Task Participating_role_changes_and_removal_invalidate_qualification_without_renewing_noops()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (administrator, target) = await SeedAsync(database);
        await using var context = database.Context();
        var members = new OrganizationMemberRepository(context);
        var organizations = new OrganizationRepository(context);

        async Task<BaseCommandResponse<Guid>> Update(UpdateOrganizationMemberRoleCommand request)
        {
            await using var session = Session(database, context, database.AdministratorId);
            await session.InitializeAsync();
            var handler = new UpdateOrganizationMemberRoleCommandHandler(members, organizations,
                new UserContext(session.Http), session.Boundary, session.Preparation);
            return await handler.ExecuteAsync(request, default);
        }

        async Task<BaseCommandResponse<Guid>> Delete(Guid memberId)
        {
            await using var session = Session(database, context, database.AdministratorId);
            await session.InitializeAsync();
            var handler = new DeleteOrganizationMemberCommandHandler(members, organizations,
                new UserContext(session.Http), session.Boundary, session.Preparation);
            return await handler.ExecuteAsync(new() { MemberId = memberId }, default);
        }

        var before = await RevisionAsync(context);
        var command = new Explore.Application.Features.OrganizationMembers.Requests.Commands.UpdateOrganizationMemberRoleCommand
        { UpdateOrganizationMemberRoleDto = new() { Id = target, Role = RoleEnum.OrgAdmin } };
        await Assert.That((await Update(command)).IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        await Assert.That((await Update(command)).IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        await Assert.That((await Delete(target)).IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 2);
        await Assert.That(await context.OrganizationMembers.AnyAsync(row => row.Id == target)).IsFalse();
        await Assert.That((await Delete(administrator)).IsSuccess).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Denied_provider_or_ordinary_member_cannot_enter_the_mutation(bool providerDenied)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target) = await SeedAsync(database);
        await using var context = database.Context();
        var actor = providerDenied ? database.AdministratorId : database.SubjectId;
        await using var session = Session(database, context, actor, !providerDenied);
        await session.InitializeAsync();
        var before = await RevisionAsync(context);
        var entered = false;
        await Assert.That(async () => await session.Boundary.ExecuteRoleChangeAsync(target, (int)RoleEnum.OrgAdmin,
            (_, _) => { entered = true; throw new InvalidOperationException("Unauthorized mutation entered."); }, default))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
        await Assert.That(entered).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
        await Assert.That(await context.OrganizationMembers.Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.OrgMember);
    }

    [Test]
    public async Task Invitation_creation_and_recipient_decline_use_the_same_native_invalidation_boundary()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target) = await SeedAsync(database);
        await using var context = database.Context();
        var members = new OrganizationMemberRepository(context);

        async Task<BaseCommandResponse<Guid>> Decline(Guid invitationId, Guid actor, bool allowed = true)
        {
            await using var session = Session(database, context, actor, allowed);
            await session.InitializeAsync();
            var handler = new DeclineInvitationCommandHandler(members, new UserContext(session.Http), session.Boundary);
            return await handler.ExecuteAsync(new() { InvitationId = invitationId }, default);
        }

        var organizationId = await context.OrganizationMembers.Where(row => row.Id == target)
            .Select(row => row.OrganizationTenant.OrganizationId).SingleAsync();
        var email = await context.Users.Where(row => row.Id == database.ReviewerId).Select(row => row.Pii!.Email).SingleAsync();
        context.ChangeTracker.Clear();
        var before = await RevisionAsync(context);
        BaseCommandResponse<Guid> invited;
        await using (var session = Session(database, context, database.AdministratorId))
        {
            await session.InitializeAsync();
            var add = new AddOrganizationMemberCommandHandler(members, new OrganizationRepository(context),
                new OrganizationTenantRepository(context), new UserRepository(context), new UserContext(session.Http), session.Boundary, session.Preparation);
            invited = await add.ExecuteAsync(new()
            {
                TenantId = database.TenantId,
                AddOrganizationMemberDto = new()
                { OrganizationId = organizationId, Email = email!, Role = RoleEnum.OrgMember }
            }, default);
        }
        await Assert.That(invited.IsSuccess).IsTrue();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        foreach (var invitationId in new[] { invited.Id, Guid.CreateVersion7(), Guid.Empty })
        {
            var rejected = await Decline(invitationId, database.AdministratorId);
            await Assert.That(rejected.IsSuccess).IsFalse();
            await Assert.That(rejected.Message).IsEqualTo("Invitation not found");
        }
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        await Assert.That(await context.OrganizationMembers.AnyAsync(row => row.Id == invited.Id)).IsTrue();
        await Assert.That((await Decline(invited.Id, database.ReviewerId, allowed: false)).IsSuccess).IsTrue();
        await Assert.That(await context.OrganizationMembers.AnyAsync(row => row.Id == invited.Id)).IsFalse();
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Changed_caller_rolls_back_membership_and_epoch_at_the_actual_commit(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target) = await SeedAsync(database);
        await using var context = database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        await using var session = CreateBoundary(context, (privacy ?? database.Privacy).Authority(context, TimeProvider.System), database.AdministratorId);
        await session.InitializeAsync();
        var before = await RevisionAsync(context);
        bool? observedOwner = null;
        await Assert.That(async () => await session.Boundary.ExecuteRoleChangeAsync(target, (int)RoleEnum.OrgAdmin,
            async (_, token) =>
            {
                observedOwner = context.IdentityFenceOwnsTransaction;
                var member = await context.OrganizationMembers.SingleAsync(row => row.Id == target, token);
                member.RoleId = (int)RoleEnum.OrgAdmin;
                await context.SaveChangesAsync(token);
                session.Http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, database.SubjectId.ToString())], "changed-membership-caller"));
                return BaseCommandResponse.Success(target);
            }, default)).Throws<Explore.Application.Exceptions.AuthorizationException>();
        await Assert.That(observedOwner).IsEqualTo(colocated);
        await Assert.That(await context.OrganizationMembers.Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.OrgMember);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task A_committed_self_demotion_retains_its_original_result_after_lost_acknowledgement(
        bool verify, bool rollBack, bool retry)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (administrator, target) = await SeedAsync(database);
        await using (var seed = database.Context())
        {
            var successor = await seed.OrganizationMembers.SingleAsync(row => row.Id == target);
            successor.RoleId = (int)RoleEnum.OrgAdmin;
            await seed.SaveChangesAsync();
        }
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, rollBack) { Armed = false };
        await using var baseline = database.Context();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = database };
        await using var session = Session(database, context, database.AdministratorId);
        await session.InitializeAsync();
        fault.Armed = true;
        var handler = new UpdateOrganizationMemberRoleCommandHandler(new OrganizationMemberRepository(context),
            new OrganizationRepository(context), new UserContext(session.Http), session.Boundary, session.Preparation);
        var before = await RevisionAsync(context);
        var result = await handler.ExecuteAsync(new()
        {
            UpdateOrganizationMemberRoleDto = new()
            { Id = administrator, Role = RoleEnum.OrgMember }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(administrator);
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.OrganizationMembers.Where(row => row.Id == administrator).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.OrgMember);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retained_erasure_prevents_new_organization_authority_before_primary_cleanup(bool erasedActor)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target) = await SeedAsync(database);
        await using var context = database.Context();
        await database.Privacy.Authority(context, TimeProvider.System).AppendAsync(PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            erasedActor ? database.AdministratorId : database.SubjectId,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await using var session = Session(database, context, database.AdministratorId);
        await session.InitializeAsync();
        var before = await RevisionAsync(context);
        var entered = false;
        await Assert.That(async () => await session.Boundary.ExecuteRoleChangeAsync(target, (int)RoleEnum.OrgAdmin,
            (_, _) => { entered = true; return Task.FromResult(BaseCommandResponse.Success(target)); }, default))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
        await Assert.That(entered).IsFalse();
        await Assert.That(await context.OrganizationMembers.Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.OrgMember);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
    }

    [Test]
    public async Task A_non_retrying_rolled_back_membership_change_preserves_the_original_failure()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target) = await SeedAsync(database);
        await using var baseline = database.Context();
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify: false, rollBack: true) { Armed = false };
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = database };
        await using var session = Session(database, context, database.AdministratorId);
        await session.InitializeAsync();
        fault.Armed = true;
        var handler = new UpdateOrganizationMemberRoleCommandHandler(new OrganizationMemberRepository(context),
            new OrganizationRepository(context), new UserContext(session.Http), session.Boundary, session.Preparation);
        var before = await RevisionAsync(context);
        await Assert.That(async () => await handler.ExecuteAsync(new()
        {
            UpdateOrganizationMemberRoleDto = new()
            { Id = target, Role = RoleEnum.OrgAdmin }
        }, default))
            .Throws<TenantUserRoleGrantMutationBoundaryTests.LostRoleCommitException>();
        await Assert.That(fault.Injected).IsTrue();
        await Assert.That(await context.OrganizationMembers.Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.OrgMember);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(before);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task<(Guid Administrator, Guid Target)> SeedAsync(SourceDatabase database)
    {
        await using var context = database.Context();
        var organization = new Organization { Id = Guid.CreateVersion7(), Pii = new() { FullName = "Native authority organization" } };
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        OrganizationMember Member(Guid user, RoleEnum role) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            UserId = user,
            User = null!,
            RoleId = (int)role,
            Role = null!
        };
        var administrator = Member(database.AdministratorId, RoleEnum.OrgAdmin);
        var target = Member(database.SubjectId, RoleEnum.OrgMember);
        context.AddRange(organization, participation, administrator, target);
        await context.SaveChangesAsync();
        return (administrator.Id, target.Id);
    }

    private static Task<long> RevisionAsync(ExploreDbContext context) => context.ModerationQualificationEpochs.AsNoTracking()
        .Select(row => row.Revision).SingleAsync();

    private static MembershipRequest<OrganizationMemberMutationBoundary> Session(
        SourceDatabase database, ExploreDbContext context, Guid actor, bool allowed = true)
        => CreateBoundary(context, database.Privacy.Authority(context, TimeProvider.System), actor, allowed);

    internal static MembershipRequest<OrganizationMemberMutationBoundary> CreateBoundary(
        ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, Guid actor, bool allowed = true)
    {
        return new(context, privacy, actor, new Provider(context, allowed), services =>
            new(context, privacy, services.GetRequiredService<IUserContext>(), services.GetRequiredService<IAdminContext>(),
                services.GetRequiredService<IMachinePrincipalAccessor>(), services.GetRequiredService<IAuthorizationProvider>(),
                services.GetRequiredService<IHumanActionCommitFinalizer>()));
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
